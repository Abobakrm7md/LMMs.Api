using System.ComponentModel.DataAnnotations;
using LMMs.Api.Application.Abstractions;
using LMMs.Api.Application.Contracts;
using LMMs.Api.Application.Exceptions;
using LMMs.Api.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace LMMs.Api.Application.Services;

public sealed class AuthService(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher<ApplicationUser> passwordHasher,
    ITokenService tokenService,
    IClock clock) : IAuthService
{
    public async Task<AuthResultDto> RegisterAsync(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim();
        var userName = command.UserName.Trim();
        var displayName = command.DisplayName.Trim();
        ValidateRegistration(email, userName, displayName, command.Password);

        var normalizedEmail = Normalize(email);
        var normalizedUserName = Normalize(userName);
        if (await userRepository.FindByNormalizedEmailAsync(normalizedEmail, cancellationToken) is not null ||
            await userRepository.FindByNormalizedUserNameAsync(normalizedUserName, cancellationToken) is not null)
        {
            throw new ConflictException("An account with that email or username already exists.");
        }

        var now = clock.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            DisplayName = displayName,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, command.Password);

        await userRepository.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToAuthResult(user, now);
    }

    public async Task<AuthResultDto> LoginAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var identifier = command.EmailOrUserName.Trim();
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(command.Password))
            throw new UnauthorizedAccessException("Invalid email/username or password.");

        var normalized = Normalize(identifier);
        var user = await userRepository.FindByNormalizedEmailAsync(normalized, cancellationToken)
                   ?? await userRepository.FindByNormalizedUserNameAsync(normalized, cancellationToken);
        if (user is null || passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password) == PasswordVerificationResult.Failed)
            throw new UnauthorizedAccessException("Invalid email/username or password.");

        var now = clock.UtcNow;
        user.LastLoginAt = now;
        user.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToAuthResult(user, now);
    }

    private AuthResultDto ToAuthResult(ApplicationUser user, DateTimeOffset now) =>
        new(tokenService.Create(user, now), new CurrentUserDto(user.Id, user.Email, user.UserName, user.DisplayName));

    private static void ValidateRegistration(string email, string userName, string displayName, string password)
    {
        if (!new EmailAddressAttribute().IsValid(email) || email.Length > 256)
            throw new RequestValidationException("A valid email address is required.");
        if (userName.Length is < 3 or > 64 || !userName.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-'))
            throw new RequestValidationException("Username must be 3-64 characters and contain only letters, numbers, '.', '_' or '-'.");
        if (displayName.Length is < 1 or > 128)
            throw new RequestValidationException("Display name must be 1-128 characters.");
        if (password.Length is < 12 or > 128)
            throw new RequestValidationException("Password must be 12-128 characters.");
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
