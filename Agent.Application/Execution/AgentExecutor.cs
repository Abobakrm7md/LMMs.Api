using Agent.Application.Planning;
using Agent.Application.Tools;
using Agent.Domain.Planning;
using Agent.Domain.Tools;

namespace Agent.Application.Execution;

public sealed class AgentExecutor(IToolRegistry registry, IPlanner planner) : IAgentExecutor
{
    private const int MaxSteps = 8;
    private const int MaxAdaptiveSteps = 3;

    public async Task<ExecutionResult> ExecuteAsync(Plan plan, IReadOnlyList<IAgentTool> tools, string userGoal, string? attachmentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.Kind == PlanKind.DirectAnswer || plan.Steps.Count == 0)
            return new ExecutionResult { Plan = plan, Completed = true };

        var remaining = new Queue<PlanStep>(plan.Steps);
        var results = new List<ToolResult>();
        var adaptiveUsed = 0;
        var completed = false;
        var aborted = false;
        string? status = null;

        while (remaining.Count > 0 && results.Count < MaxSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = PlanStepResolver.Resolve(remaining.Dequeue(), results);
            if (string.IsNullOrWhiteSpace(step.Tool) || PlanStepResolver.HasUnresolvedPlaceholders(step) || PlanStepResolver.NeedsCalculatorRewrite(step))
            {
                var reason = PlanStepResolver.NeedsCalculatorRewrite(step)
                    ? "Calculator requires one math expression using prior numeric results. For a date, use the day of month."
                    : "Step input is incomplete or invalid";
                var directive = await AdviseAsync(plan, userGoal, results, step, reason, tools, cancellationToken);
                if (!Apply(directive, remaining, results, ref adaptiveUsed, ref completed, ref aborted, ref status)) break;
                continue;
            }

            var tool = registry.Find(step.Tool, tools);
            ToolResult result;
            if (tool is null)
                result = ToolResult.Failure(step.Id, step.Description, step.Tool, step.Input, $"Unknown tool '{step.Tool}'");
            else
            {
                try
                {
                    var executed = await tool.ExecuteAsync(new ToolContext(step.Input, attachmentId), cancellationToken);
                    result = CopyForStep(executed, step, tool.Definition.Name);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result = ToolResult.Failure(step.Id, step.Description, tool.Definition.Name, step.Input, ex.GetBaseException().Message);
                }
            }
            results.Add(result);
            if (result.Succeeded) continue;

            var advice = await AdviseAsync(plan, userGoal, results, step, result.Error ?? "Tool failed", tools, cancellationToken);
            if (!Apply(advice, remaining, results, ref adaptiveUsed, ref completed, ref aborted, ref status)) break;
        }

        if (!aborted && !completed) completed = results.Count > 0 && results.All(r => r.Succeeded) && remaining.Count == 0;
        return new ExecutionResult { Plan = plan, StepResults = results, Completed = completed && !aborted, Aborted = aborted, StatusDetail = status };
    }

    private Task<ExecutionDirective> AdviseAsync(Plan plan, string goal, IReadOnlyList<ToolResult> results, PlanStep step, string reason, IReadOnlyList<IAgentTool> tools, CancellationToken ct) =>
        planner.DecideNextAsync(new ExecutionState(goal, plan, results, step, reason, tools.Select(t => t.Definition).ToList()), ct);

    private static bool Apply(ExecutionDirective directive, Queue<PlanStep> remaining, List<ToolResult> results, ref int adaptiveUsed, ref bool completed, ref bool aborted, ref string? status)
    {
        status = directive.Reason;
        switch (directive.Action)
        {
            case ExecutionAction.Complete: remaining.Clear(); completed = true; return false;
            case ExecutionAction.Abort: remaining.Clear(); aborted = true; return false;
            case ExecutionAction.Continue: return remaining.Count > 0;
            case ExecutionAction.ExecuteStep when directive.Step is not null:
                if (adaptiveUsed >= MaxAdaptiveSteps) { aborted = true; status = "Adaptive step limit reached"; return false; }
                adaptiveUsed++;
                var next = PlanStepResolver.Resolve(CloneStep(directive.Step, results), results);
                var replay = remaining.ToArray(); remaining.Clear(); remaining.Enqueue(next);
                foreach (var item in replay) remaining.Enqueue(item);
                return true;
            default: aborted = true; return false;
        }
    }

    private static PlanStep CloneStep(PlanStep step, IReadOnlyList<ToolResult> results) => new()
    {
        Id = step.Id <= 0 || results.Any(r => r.StepId == step.Id) ? (results.Count == 0 ? 1 : results.Max(r => r.StepId) + 1) : step.Id,
        Description = step.Description, Tool = step.Tool, Input = step.Input
    };

    private static ToolResult CopyForStep(ToolResult source, PlanStep step, string toolName) => source.Succeeded
        ? ToolResult.Success(step.Id, step.Description, toolName, step.Input, source.Output)
        : ToolResult.Failure(step.Id, step.Description, toolName, step.Input, source.Error ?? "Tool failed", source.Output);
}
