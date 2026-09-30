using Agent.Domain.Persistence;

namespace Agent.Application.Authentication;

public interface IPasswordService
{
    string Hash(ApplicationUser user, string password);
    bool Verify(ApplicationUser user, string passwordHash, string password);
}
