namespace LMMs.Api.Planning
{
    public interface IPlanner
    {
        Task<AgentPlan> CreatePlanAsync(PlanningRequest request, CancellationToken cancellationToken);

        Task<ExecutionDirective> DecideNextAsync(ExecutionState state, CancellationToken cancellationToken);
    }
}
