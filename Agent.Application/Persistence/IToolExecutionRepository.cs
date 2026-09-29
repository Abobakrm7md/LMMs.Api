using Agent.Domain.Persistence;

namespace Agent.Application.Persistence;

public interface IToolExecutionRepository
{
    Task AddAsync(ToolExecution execution, CancellationToken cancellationToken);
}
