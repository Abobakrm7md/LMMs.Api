using LMMs.Api.Application.Contracts;
using LMMs.Api.Planning;

namespace LMMs.Api.Agents;

public sealed record AgentTurnRequest(
    string Prompt,
    IReadOnlyList<AgentHistoryMessage> History,
    bool EnableTools,
    bool EnableWebSearch);

public abstract record AgentTurnEvent;
public sealed record ToolExecutionCompleted(ToolExecutionResult Result) : AgentTurnEvent;
public sealed record TextDeltaProduced(string Text) : AgentTurnEvent;
public sealed record AgentTurnCompleted(string? ModelName = null) : AgentTurnEvent;

public interface IAgentTurnRunner
{
    IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken);
}
