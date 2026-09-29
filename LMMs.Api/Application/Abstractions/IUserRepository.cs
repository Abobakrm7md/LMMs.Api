using LMMs.Api.Domain.Entities;

namespace LMMs.Api.Application.Abstractions;

public interface IUserRepository
{
    Task<ApplicationUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<ApplicationUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken cancellationToken);
    Task AddAsync(ApplicationUser user, CancellationToken cancellationToken);
}
