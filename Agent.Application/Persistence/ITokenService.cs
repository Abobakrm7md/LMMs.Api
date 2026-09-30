using Agent.Application.Authentication;
using Agent.Domain.Persistence;

namespace Agent.Application.Persistence;

public interface ITokenService
{
    AccessToken Create(ApplicationUser user, DateTimeOffset now);
}
