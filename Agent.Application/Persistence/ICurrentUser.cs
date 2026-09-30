namespace Agent.Application.Persistence;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Email { get; }
    string? UserName { get; }
}
