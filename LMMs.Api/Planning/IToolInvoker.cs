using LMMs.Api.Interfaces;

namespace LMMs.Api.Planning
{
    public interface IToolInvoker
    {
        Task<ToolExecutionResult> InvokeAsync(
            PlanStep step,
            IReadOnlyList<IAgentTool> tools,
            CancellationToken cancellationToken);
    }
}
