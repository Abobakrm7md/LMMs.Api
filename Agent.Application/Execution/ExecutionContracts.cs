using Agent.Application.Tools;
using Agent.Domain.Planning;
using Agent.Domain.Tools;
namespace Agent.Application.Execution;
public interface IAgentExecutor
{
    Task<ExecutionResult> ExecuteAsync(Plan plan, IReadOnlyList<IAgentTool> tools, string userGoal, string? attachmentId, CancellationToken cancellationToken);
}
