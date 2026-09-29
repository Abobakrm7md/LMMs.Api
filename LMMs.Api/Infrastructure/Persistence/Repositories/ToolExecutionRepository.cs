using LMMs.Api.Application.Abstractions;
using LMMs.Api.Domain.Entities;

namespace LMMs.Api.Infrastructure.Persistence.Repositories;

public sealed class ToolExecutionRepository(AppDbContext dbContext) : IToolExecutionRepository
{
    public Task AddAsync(ToolExecution execution, CancellationToken cancellationToken) =>
        dbContext.ToolExecutions.AddAsync(execution, cancellationToken).AsTask();
}
