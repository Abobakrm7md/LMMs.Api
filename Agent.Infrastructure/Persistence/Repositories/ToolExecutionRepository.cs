using Agent.Application.Persistence;
using Agent.Domain.Persistence;

namespace Agent.Infrastructure.Persistence.Repositories;

public sealed class ToolExecutionRepository(AppDbContext dbContext) : IToolExecutionRepository
{
    public Task AddAsync(ToolExecution execution, CancellationToken cancellationToken) =>
        dbContext.ToolExecutions.AddAsync(execution, cancellationToken).AsTask();
}
