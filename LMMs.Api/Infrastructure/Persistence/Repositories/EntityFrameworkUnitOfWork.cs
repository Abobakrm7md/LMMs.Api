using LMMs.Api.Application.Abstractions;

namespace LMMs.Api.Infrastructure.Persistence.Repositories;

public sealed class EntityFrameworkUnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
