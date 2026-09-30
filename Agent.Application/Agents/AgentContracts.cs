using Agent.Domain.Conversations;

namespace Agent.Application.Agents;

public sealed record AgentRequest(
    string Prompt,
    string? AttachmentId = null,
    string? AttachmentName = null);

public interface IAgent
{
    string Name { get; }
    IAsyncEnumerable<string> RunAsync(AgentRequest request, Conversation conversation, CancellationToken cancellationToken);
}
