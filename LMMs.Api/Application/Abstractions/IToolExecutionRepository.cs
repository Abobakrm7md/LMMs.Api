using LMMs.Api.Domain.Entities;

namespace LMMs.Api.Application.Abstractions;

public interface IToolExecutionRepository
{
    Task AddAsync(ToolExecution execution, CancellationToken cancellationToken);
}
