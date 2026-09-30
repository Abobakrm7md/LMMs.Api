using Agent.Application.Conversations;
using Agent.Domain.Tools;

namespace Agent.Application.Agents;

public sealed record AgentTurnRequest(
    string Prompt,
    IReadOnlyList<AgentHistoryMessage> History,
    string? AttachmentId = null,
    string? AttachmentName = null);

public abstract record AgentTurnEvent;
public sealed record ToolExecutionCompleted(ToolResult Result) : AgentTurnEvent;
public sealed record TextDeltaProduced(string Text) : AgentTurnEvent;
public sealed record AgentTurnCompleted(string? ModelName = null) : AgentTurnEvent;

public interface IAgentTurnRunner
{
    IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken);
}
