using Agent.Application.Authentication;
using Agent.Domain.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Agent.Infrastructure.Authentication;

public sealed class IdentityPasswordService(IPasswordHasher<ApplicationUser> hasher) : IPasswordService
{
    public string Hash(ApplicationUser user, string password) => hasher.HashPassword(user, password);

    public bool Verify(ApplicationUser user, string passwordHash, string password) =>
        hasher.VerifyHashedPassword(user, passwordHash, password) != PasswordVerificationResult.Failed;
}
