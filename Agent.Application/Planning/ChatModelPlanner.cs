using System.Text;
using Agent.Application.Chat;
using Agent.Application.Tools;
using Agent.Domain.Conversations;
using Agent.Domain.Planning;

namespace Agent.Application.Planning;

public sealed class ChatModelPlanner(IChatModel chatModel) : IPlanner
{
    public async Task<Plan> CreatePlanAsync(PlanningRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Tools.Count == 0) return Plan.Direct("Answer from conversation and model knowledge");
        try
        {
            var messages = new[]
            {
                new Message(MessageRole.System, BuildPlanPrompt(request.Tools)),
                new Message(MessageRole.User, BuildUserPrompt(request))
            };
            var response = await chatModel.CompleteAsync(messages, new ChatModelOptions(0), cancellationToken);
            return PlanJsonParser.ParsePlan(response);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Plan.Direct("Answer the user; planning failed"); }
    }

    public async Task<ExecutionDirective> DecideNextAsync(ExecutionState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var messages = new[]
            {
                new Message(MessageRole.System, BuildAdvicePrompt(state.Tools)),
                new Message(MessageRole.User, BuildAdviceContext(state))
            };
            var response = await chatModel.CompleteAsync(messages, new ChatModelOptions(0), cancellationToken);
            var nextId = state.ResultsSoFar.Count == 0 ? 1 : state.ResultsSoFar.Max(r => r.StepId) + 1;
            return PlanJsonParser.ParseDirective(response, nextId);
        }
        catch (OperationCanceledException) { throw; }
        catch { return ExecutionDirective.Abort("Could not decide the next action"); }
    }

    private static string BuildPlanPrompt(IReadOnlyList<ToolDefinition> tools)
    {
        var p1 = "{{1}}";
        return $$"""
        You are a planning component for an AI agent. Return ONLY JSON; no markdown or hidden reasoning.
        Decide whether the task can be answered directly, needs one tool, or needs sequential tools.
        Available tools:
        {{FormatTools(tools)}}
        Use tools only when necessary. Use exact tool and parameter names. Calculator input must be one math expression.
        Later steps may reference earlier output with {{p1}}. Use * for multiplication.
        JSON: { "goal":"short goal", "kind":"direct|single_tool|multi_step", "steps":[{"id":1,"description":"task","tool":"exact_name","input":{}}] }
        For direct answers, steps must be []. Do not add unnecessary or repeated calls.
        """;
    }

    private static string BuildUserPrompt(PlanningRequest request) => $"""
        Recent conversation:
        {(request.RecentConversation.Count == 0 ? "(none)" : string.Join("\n", request.RecentConversation.TakeLast(6)))}
        User task:
        {request.UserPrompt}
        """;

    private static string BuildAdvicePrompt(IReadOnlyList<ToolDefinition> tools) => $$"""
        Decide the next execution action. Return ONLY JSON and no hidden reasoning.
        Available tools:
        {{FormatTools(tools)}}
        Actions are continue, execute_step, complete, abort.
        JSON: { "action":"continue|execute_step|complete|abort", "reason":"short status", "tool":"tool_name", "description":"step", "input":{} }
        Do not repeat successful calls. Calculator input must be one expression. A date's day may be used as a number when requested.
        """;

    private static string BuildAdviceContext(ExecutionState state)
    {
        var text = new StringBuilder($"Goal: {state.UserGoal}\nPlan goal: {state.Plan.Goal}\nReason: {state.Reason}\nResults:\n");
        foreach (var result in state.ResultsSoFar)
            text.AppendLine($"- step {result.StepId}; tool={result.ToolName}; succeeded={result.Succeeded}; output={Truncate(result.Output, 500)}; error={result.Error}");
        if (state.ResultsSoFar.Count == 0) text.AppendLine("(none)");
        return text.ToString();
    }

    private static string FormatTools(IEnumerable<ToolDefinition> tools) => string.Join("\n", tools.Select(t =>
        $"- {t.Name}: {t.Description} (parameters: {(t.Parameters.Count == 0 ? "none" : string.Join(", ", t.Parameters))})"));
    private static string Truncate(string? value, int max) => string.IsNullOrEmpty(value) || value.Length <= max ? value ?? "" : value[..max] + "...";
}
