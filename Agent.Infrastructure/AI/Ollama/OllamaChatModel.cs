using System.Runtime.CompilerServices;
using Agent.Application.Chat;
using Agent.Domain.Conversations;
using Microsoft.Extensions.AI;
using DomainMessage = Agent.Domain.Conversations.Message;

namespace Agent.Infrastructure.AI.Ollama;

public sealed class OllamaChatModel(IChatClient client) : IChatModel
{
    public async Task<string> CompleteAsync(IReadOnlyList<DomainMessage> messages, ChatModelOptions options, CancellationToken cancellationToken)
    {
        var response = await client.GetResponseAsync(Map(messages), Options(options), cancellationToken);
        return response.Text ?? string.Empty;
    }

    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<DomainMessage> messages, ChatModelOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in client.GetStreamingResponseAsync(Map(messages), Options(options), cancellationToken).WithCancellation(cancellationToken))
            if (!string.IsNullOrEmpty(update.Text)) yield return update.Text;
    }

    private static List<ChatMessage> Map(IEnumerable<DomainMessage> messages) => messages.Select(m => new ChatMessage(m.Role switch
    {
        MessageRole.System => ChatRole.System,
        MessageRole.User => ChatRole.User,
        MessageRole.Assistant => ChatRole.Assistant,
        MessageRole.Tool => ChatRole.Tool,
        _ => ChatRole.User
    }, m.Content)).ToList();

    private static ChatOptions Options(ChatModelOptions options) => new() { Tools = [], ToolMode = ChatToolMode.None, Temperature = options.Temperature };
}
