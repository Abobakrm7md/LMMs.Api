using Agent.Application.Persistence;

namespace Agent.Infrastructure.Persistence.Repositories;

public sealed class EntityFrameworkUnitOfWork(AppDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
