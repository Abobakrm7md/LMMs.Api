namespace Agent.Application.Authentication;

public sealed record RegisterUserCommand(string Email, string UserName, string DisplayName, string Password);
public sealed record LoginCommand(string EmailOrUserName, string Password);
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
public sealed record CurrentUserDto(Guid Id, string Email, string UserName, string DisplayName);
public sealed record AuthResultDto(AccessToken Token, CurrentUserDto User);
