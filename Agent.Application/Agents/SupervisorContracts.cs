namespace Agent.Application.Agents;

public sealed record AgentDescriptor(string Name, string Description, IReadOnlyList<string> Capabilities);

public interface ISpecializedAgent
{
    AgentDescriptor Descriptor { get; }
    bool CanHandle(AgentTurnRequest request);
    IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken);
}

public interface IAgentRegistry
{
    IReadOnlyList<ISpecializedAgent> Agents { get; }
    ISpecializedAgent? Find(AgentTurnRequest request);
}

public sealed class AgentRegistry(IEnumerable<ISpecializedAgent> agents) : IAgentRegistry
{
    public IReadOnlyList<ISpecializedAgent> Agents { get; } = agents.ToList();

    public ISpecializedAgent? Find(AgentTurnRequest request) =>
        Agents.FirstOrDefault(agent => agent.CanHandle(request));
}

public sealed class NoSuitableAgentException(string message) : InvalidOperationException(message);

public sealed class SupervisorAgent(IAgentRegistry registry) : IAgentTurnRunner
{
    public async IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(
        AgentTurnRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var agent = registry.Find(request)
            ?? throw new NoSuitableAgentException($"No specialized agent can handle this request.");

        await foreach (var result in agent.RunTurnAsync(request, cancellationToken).WithCancellation(cancellationToken))
            yield return result;
    }
}
