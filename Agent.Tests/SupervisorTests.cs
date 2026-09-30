using System.Runtime.CompilerServices;
using Agent.Application.Agents;
using Agent.Application.Conversations;
using Xunit;

namespace Agent.Tests;

public sealed class SupervisorTests
{
    [Fact]
    public async Task Registry_discovers_and_supervisor_delegates_matching_agent()
    {
        var agent = new FakeSpecializedAgent(true);
        var supervisor = new SupervisorAgent(new AgentRegistry([agent]));
        var events = await Collect(supervisor.RunTurnAsync(new AgentTurnRequest("research", []), default));
        Assert.Equal("research", agent.Received?.Prompt);
        Assert.IsType<TextDeltaProduced>(Assert.Single(events));
    }

    [Fact]
    public void Registry_returns_null_when_no_agent_matches()
    {
        var registry = new AgentRegistry([new FakeSpecializedAgent(false)]);
        Assert.Null(registry.Find(new AgentTurnRequest("coding", [])));
        Assert.Single(registry.Agents);
    }

    [Fact]
    public async Task Supervisor_reports_agent_failures_without_swallowing_them()
    {
        var supervisor = new SupervisorAgent(new AgentRegistry([new FailingAgent()]));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Collect(supervisor.RunTurnAsync(new AgentTurnRequest("fail", []), default)));
    }

    private static async Task<List<AgentTurnEvent>> Collect(IAsyncEnumerable<AgentTurnEvent> stream)
    {
        var result = new List<AgentTurnEvent>();
        await foreach (var item in stream) result.Add(item);
        return result;
    }

    private sealed class FakeSpecializedAgent(bool canHandle) : ISpecializedAgent
    {
        public AgentTurnRequest? Received { get; private set; }
        public AgentDescriptor Descriptor { get; } = new("fake", "test", ["test"]);
        public bool CanHandle(AgentTurnRequest request) => canHandle;
        public async IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Received = request;
            yield return new TextDeltaProduced("delegated");
            await Task.CompletedTask;
        }
    }

    private sealed class FailingAgent : ISpecializedAgent
    {
        public AgentDescriptor Descriptor { get; } = new("failing", "test", ["test"]);
        public bool CanHandle(AgentTurnRequest request) => true;
        public async IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(AgentTurnRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("agent failed");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
