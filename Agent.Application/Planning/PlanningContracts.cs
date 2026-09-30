using Agent.Application.Tools;
using Agent.Domain.Planning;
using Agent.Domain.Tools;
namespace Agent.Application.Planning;

public sealed record PlanningRequest(string UserPrompt, IReadOnlyList<string> RecentConversation, IReadOnlyList<ToolDefinition> Tools);
public sealed record ExecutionState(string UserGoal, Plan Plan, IReadOnlyList<ToolResult> ResultsSoFar, PlanStep? CurrentStep, string Reason, IReadOnlyList<ToolDefinition> Tools);
public interface IPlanner
{
    Task<Plan> CreatePlanAsync(PlanningRequest request, CancellationToken cancellationToken);
    Task<ExecutionDirective> DecideNextAsync(ExecutionState state, CancellationToken cancellationToken);
}
