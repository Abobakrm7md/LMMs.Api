using Agent.Domain.Conversations;

namespace Agent.Application.Chat;

public sealed record ChatModelOptions(float Temperature = 0.1f);

public interface IChatModel
{
    Task<string> CompleteAsync(IReadOnlyList<Message> messages, ChatModelOptions options, CancellationToken cancellationToken);
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<Message> messages, ChatModelOptions options, CancellationToken cancellationToken);
}
