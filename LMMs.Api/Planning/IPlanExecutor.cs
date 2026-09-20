using LMMs.Api.Interfaces;

namespace LMMs.Api.Planning
{
    public interface IPlanExecutor
    {
        Task<PlanExecutionResult> ExecuteAsync(
            AgentPlan plan,
            IReadOnlyList<IAgentTool> tools,
            string userGoal,
            CancellationToken cancellationToken);
    }
}
