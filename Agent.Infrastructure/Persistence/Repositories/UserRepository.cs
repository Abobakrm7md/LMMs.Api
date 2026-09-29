using Agent.Application.Persistence;
using Agent.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Agent.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AppDbContext dbContext) : IUserRepository
{
    public Task<ApplicationUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<ApplicationUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(user => user.NormalizedUserName == normalizedUserName, cancellationToken);

    public Task AddAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        dbContext.Users.AddAsync(user, cancellationToken).AsTask();
}
