using LMMs.Api.Application.Contracts;

namespace LMMs.Api.Application.Abstractions;

public interface IAuthService
{
    Task<AuthResultDto> RegisterAsync(RegisterUserCommand command, CancellationToken cancellationToken);
    Task<AuthResultDto> LoginAsync(LoginCommand command, CancellationToken cancellationToken);
}
