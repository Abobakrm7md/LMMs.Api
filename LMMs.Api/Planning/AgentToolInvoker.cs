using System.Globalization;
using System.Reflection;
using System.Text.Json;
using LMMs.Api.Interfaces;

namespace LMMs.Api.Planning
{
    public sealed class AgentToolInvoker : IToolInvoker
    {
        private readonly ILogger<AgentToolInvoker> _logger;

        public AgentToolInvoker(ILogger<AgentToolInvoker> logger)
        {
            _logger = logger;
        }

        public async Task<ToolExecutionResult> InvokeAsync(
            PlanStep step,
            IReadOnlyList<IAgentTool> tools,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var arguments = step.Input ?? new Dictionary<string, object?>();
            var toolName = step.Tool;

            if (string.IsNullOrWhiteSpace(toolName))
            {
                _logger.LogWarning("Step {StepId} is missing a tool name", step.Id);
                return ToolExecutionResult.Failure(step.Id, step.Description, toolName, arguments, "Invalid plan: step has no tool");
            }

            var tool = FindTool(tools, toolName);
            if (tool is null)
            {
                _logger.LogWarning("Step {StepId} requested unknown tool {Tool}", step.Id, toolName);
                return ToolExecutionResult.Failure(step.Id, step.Description, toolName, arguments, $"Unknown tool '{toolName}'");
            }

            _logger.LogInformation("Executing tool {Tool} for step {StepId}", tool.Name, step.Id);

            try
            {
                var function = tool.GetFunction();
                var bound = BindArguments(function.Method, arguments, cancellationToken);
                var raw = function.DynamicInvoke(bound);
                var output = await UnwrapAsync(raw, cancellationToken);

                if (LooksLikeFailure(output))
                {
                    _logger.LogWarning("Tool {Tool} for step {StepId} returned a failure result ({Length} chars)",
                        tool.Name, step.Id, output?.Length ?? 0);
                    return ToolExecutionResult.Failure(step.Id, step.Description, tool.Name, arguments, output ?? "Tool failed", output);
                }

                _logger.LogInformation("Tool {Tool} for step {StepId} completed ({Length} chars)",
                    tool.Name, step.Id, output?.Length ?? 0);

                return ToolExecutionResult.Success(step.Id, step.Description, tool.Name, arguments, output ?? string.Empty);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Tool {Tool} for step {StepId} was cancelled", tool.Name, step.Id);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tool {Tool} for step {StepId} failed", tool.Name, step.Id);
                return ToolExecutionResult.Failure(step.Id, step.Description, tool.Name, arguments, ex.GetBaseException().Message);
            }
        }

        private static IAgentTool? FindTool(IReadOnlyList<IAgentTool> tools, string name)
        {
            var normalized = Normalize(name);
            return tools.FirstOrDefault(t =>
                Normalize(t.Name) == normalized ||
                Normalize(t.GetFunction().Method.Name) == normalized);
        }

        private static string Normalize(string name) =>
            name.Trim().ToLowerInvariant() switch
            {
                "calculator" or "calc" or "math" => "calculator",
                "gettime" or "time" or "clock" => "gettime",
                "getdate" or "date" or "day" or "datetime" => "getdate",
                "read_file" or "readfile" or "file" => "read_file",
                "web_search" or "websearch" or "search" => "web_search",
                var other => other.Replace(" ", string.Empty)
            };

        private static object?[] BindArguments(
            MethodInfo method,
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken)
        {
            var parameters = method.GetParameters();
            var bound = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    bound[i] = cancellationToken;
                    continue;
                }

                if (TryGetArgument(arguments, parameter.Name, out var value) ||
                    (parameters.Count(p => p.ParameterType != typeof(CancellationToken)) == 1
                     && TryGetSingleValue(arguments, out value)))
                {
                    bound[i] = ConvertArgument(value, parameter.ParameterType);
                }
                else if (parameter.HasDefaultValue)
                {
                    bound[i] = parameter.DefaultValue;
                }
                else if (parameter.ParameterType.IsValueType)
                {
                    bound[i] = Activator.CreateInstance(parameter.ParameterType);
                }
            }

            return bound;
        }

        private static bool TryGetArgument(
            IReadOnlyDictionary<string, object?> arguments,
            string? name,
            out object? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (arguments.TryGetValue(name, out value))
                return true;

            foreach (var pair in arguments)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetSingleValue(IReadOnlyDictionary<string, object?> arguments, out object? value)
        {
            if (arguments.Count == 1)
            {
                value = arguments.Values.First();
                return true;
            }

            if (arguments.TryGetValue("input", out value) || arguments.TryGetValue("query", out value))
                return true;

            value = null;
            return false;
        }

        private static object? ConvertArgument(object? value, Type targetType)
        {
            if (value is null)
                return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

            if (targetType.IsInstanceOfType(value))
                return value;

            if (value is JsonElement json)
                value = json.ValueKind == JsonValueKind.String ? json.GetString() : json.ToString();

            if (targetType == typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            try
            {
                return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                return value.ToString();
            }
        }

        private static async Task<string?> UnwrapAsync(object? raw, CancellationToken cancellationToken)
        {
            switch (raw)
            {
                case null:
                    return string.Empty;
                case string s:
                    return s;
                case Task<string> ts:
                    return await ts.WaitAsync(cancellationToken);
                case Task task:
                    await task.WaitAsync(cancellationToken);
                    var resultProperty = task.GetType().GetProperty("Result");
                    return resultProperty?.GetValue(task)?.ToString() ?? string.Empty;
                default:
                    return raw.ToString();
            }
        }

        private static bool LooksLikeFailure(string? output)
        {
            if (string.IsNullOrWhiteSpace(output))
                return true;

            return output.StartsWith("Invalid calculation", StringComparison.OrdinalIgnoreCase)
                   || output.StartsWith("Unknown tool", StringComparison.OrdinalIgnoreCase)
                   || output.StartsWith("Tool error", StringComparison.OrdinalIgnoreCase);
        }
    }
}
