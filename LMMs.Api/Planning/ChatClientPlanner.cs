using System.Text;
using Microsoft.Extensions.AI;

namespace LMMs.Api.Planning
{
    public sealed class ChatClientPlanner : IPlanner
    {
        private readonly IChatClient _chatClient;
        private readonly ILogger<ChatClientPlanner> _logger;

        public ChatClientPlanner(IChatClient chatClient, ILogger<ChatClientPlanner> logger)
        {
            _chatClient = chatClient;
            _logger = logger;
        }

        public async Task<AgentPlan> CreatePlanAsync(PlanningRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Tools.Count == 0)
            {
                _logger.LogInformation("Plan creation skipped; no tools are available");
                return AgentPlan.Direct("Answer from conversation and model knowledge");
            }

            _logger.LogInformation("Creating execution plan with {ToolCount} available tools", request.Tools.Count);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildPlanSystemPrompt(request.Tools)),
                new(ChatRole.User, BuildPlanUserPrompt(request))
            };

            var options = new ChatOptions
            {
                Tools = [],
                ToolMode = ChatToolMode.None,
                Temperature = 0f
            };

            try
            {
                var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
                var plan = PlanJsonParser.ParsePlan(response.Text);
                _logger.LogInformation(
                    "Plan created: kind={Kind}, steps={StepCount}, tools={Tools}",
                    plan.Kind,
                    plan.Steps.Count,
                    string.Join(",", plan.Steps.Select(s => s.Tool)));
                return plan;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Plan creation was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plan creation failed; falling back to a direct answer");
                return AgentPlan.Direct("Answer the user; planning failed");
            }
        }

        public async Task<ExecutionDirective> DecideNextAsync(ExecutionState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Requesting next execution action after {ResultCount} step(s)", state.ResultsSoFar.Count);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, BuildAdviceSystemPrompt(state.Tools)),
                new(ChatRole.User, BuildAdviceUserPrompt(state))
            };

            var options = new ChatOptions
            {
                Tools = [],
                ToolMode = ChatToolMode.None,
                Temperature = 0f
            };

            try
            {
                var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
                var nextId = state.ResultsSoFar.Count == 0 ? 1 : state.ResultsSoFar.Max(r => r.StepId) + 1;
                var directive = PlanJsonParser.ParseDirective(response.Text, nextId);
                _logger.LogInformation("Next action decided: {Action}", directive.Action);
                return directive;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Next-action decision failed");
                return ExecutionDirective.Abort("Could not decide the next action");
            }
        }

        private static string BuildPlanSystemPrompt(IReadOnlyList<ToolDescriptor> tools)
        {
            var catalog = FormatTools(tools);
            var p1 = "{{1}}";
            var p2 = "{{2}}";
            var pStep = "{{step:1}}";
            return $$"""
                You are a planning component for an AI agent.
                Return ONLY JSON. No markdown. No explanation. No hidden reasoning.

                Decide whether the user task:
                - can be answered directly
                - needs one tool
                - needs multiple sequential tools

                Available tools:
                {{catalog}}

                Use tools only when necessary (exact math, current date/time, attached file, live web facts).
                Do not add extra steps. Do not call the same tool twice unless required.

                Tool input rules:
                - Use each tool's real parameter names. Calculator has one parameter: input.
                - Calculator input MUST be a single math expression string, never an object.
                - Later steps may reference earlier results with {{p1}} or {{pStep}}.
                - If a later Calculator step needs previous values, write them into the expression, e.g. "input": "{{p1}} + {{p2}}".
                - Never write Calculator input like {"result":"step1.result","day_of_month":"step2"}.
                - Use * for multiplication, not ×.

                JSON shape:
                {
                  "goal": "short goal",
                  "kind": "direct" | "single_tool" | "multi_step",
                  "steps": [
                    {
                      "id": 1,
                      "description": "what this step does",
                      "tool": "exact_tool_name",
                      "input": {}
                    }
                  ]
                }

                Example for math then date then add the day:
                {
                  "goal": "Multiply then add today's day of month",
                  "kind": "multi_step",
                  "steps": [
                    { "id": 1, "description": "Multiply 25 * 4", "tool": "Calculator", "input": { "input": "25 * 4" } },
                    { "id": 2, "description": "Get today's date", "tool": "GetDate", "input": {} },
                    { "id": 3, "description": "Add the day of month", "tool": "Calculator", "input": { "input": "{{p1}} + {{p2}}" } }
                  ]
                }

                If kind is direct, steps must be [].
                """;
        }

        private static string BuildPlanUserPrompt(PlanningRequest request)
        {
            var history = request.RecentConversation.Count == 0
                ? "(none)"
                : string.Join("\n", request.RecentConversation.TakeLast(6));

            return $"""
                Recent conversation (truncated, no secrets):
                {history}

                User task:
                {request.UserPrompt}
                """;
        }

        private static string BuildAdviceSystemPrompt(IReadOnlyList<ToolDescriptor> tools) =>
           $$"""
            You decide the next execution action. Return ONLY JSON. No markdown. No hidden reasoning.

            Available tools:
            {{FormatTools(tools)}}

            Actions:
            - continue: keep the remaining static plan
            - execute_step: run one revised tool step
            - complete: the goal is already satisfied; no more tools
            - abort: cannot continue

            JSON:
            {
              "action": "continue" | "execute_step" | "complete" | "abort",
              "reason": "short status",
              "tool": "tool_name",
              "description": "step description",
              "input": {}
            }

            Do not repeat a successful tool call. Prefer complete over extra tools.
            If rewriting a Calculator step, set input to a single expression such as "100 + 17".
            If a prior tool returned a date and the user asked for the day of the month, use that day as a number.
            """;

        private static string BuildAdviceUserPrompt(ExecutionState state)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Goal: {state.UserGoal}");
            sb.AppendLine($"Plan goal: {state.Plan.Goal}");
            sb.AppendLine($"Reason for advice: {state.Reason}");
            sb.AppendLine("Results so far:");
            if (state.ResultsSoFar.Count == 0)
                sb.AppendLine("(none)");
            else
            {
                foreach (var result in state.ResultsSoFar)
                {
                    sb.AppendLine($"- step {result.StepId} tool={result.ToolName} succeeded={result.Succeeded} empty={result.IsEmpty}");
                    sb.AppendLine($"  output: {Truncate(result.Output, 500)}");
                    if (!string.IsNullOrWhiteSpace(result.Error))
                        sb.AppendLine($"  error: {result.Error}");
                }
            }

            return sb.ToString();
        }

        private static string FormatTools(IReadOnlyList<ToolDescriptor> tools)
        {
            if (tools.Count == 0)
                return "(none)";

            return string.Join("\n", tools.Select(t =>
            {
                var parameters = t.Parameters.Count == 0 ? "none" : string.Join(", ", t.Parameters);
                return $"- {t.Name}: {t.Description} (parameters: {parameters})";
            }));
        }

        private static string Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
                return value ?? string.Empty;
            return value[..max] + "...";
        }
    }
}
