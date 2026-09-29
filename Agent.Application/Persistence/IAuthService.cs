using Agent.Application.Authentication;

namespace Agent.Application.Persistence;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(RegisterUserCommand command, CancellationToken cancellationToken);
    Task<AuthResultDto> LoginAsync(LoginCommand command, CancellationToken cancellationToken);
}
