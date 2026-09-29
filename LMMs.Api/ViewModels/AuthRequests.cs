using System.ComponentModel.DataAnnotations;

namespace LMMs.Api.ViewModels;

public sealed class RegisterRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(64, MinimumLength = 3)]
    public string UserName { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required, StringLength(256)]
    public string EmailOrUserName { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}
