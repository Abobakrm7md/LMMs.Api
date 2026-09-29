using System.Security.Claims;
using LMMs.Api.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;

namespace LMMs.Api.Infrastructure.Authentication;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);
    public string? UserName => Principal?.FindFirstValue(ClaimTypes.Name);
}
