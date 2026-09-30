using Agent.Application.Persistence;
using Agent.Application.Authentication;
using Agent.Application.Common;
using Agent.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthResultDto>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await authService.RegisterAsync(
                new RegisterUserCommand(request.Email, request.UserName, request.DisplayName, request.Password),
                cancellationToken);
            return Created(string.Empty, result);
        }
        catch (ConflictException exception)
        {
            return Conflict(new ProblemDetails { Title = exception.Message, Status = StatusCodes.Status409Conflict });
        }
        catch (RequestValidationException exception)
        {
            return ValidationProblem(detail: exception.Message);
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResultDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.LoginAsync(
                new LoginCommand(request.EmailOrUserName, request.Password),
                cancellationToken));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Invalid email/username or password.",
                Status = StatusCodes.Status401Unauthorized
            });
        }
    }
}
