using System.Runtime.CompilerServices;
using Agent.Application.Agents;
using Agent.Application.Chat;
using Agent.Application.Execution;
using Agent.Application.Planning;
using Agent.Application.Tools;
using Agent.Domain.Conversations;
using Agent.Domain.Planning;
using Agent.Domain.Tools;
using Xunit;

namespace Agent.Tests;

public sealed class DomainTests
{
    [Fact] public void Conversation_TrimsOldestMessages()
    {
        var conversation = new Conversation();
        conversation.Add(new Message(MessageRole.User, "one"));
        conversation.Add(new Message(MessageRole.Assistant, "two"));
        conversation.TrimToLast(1);
        Assert.Equal("two", Assert.Single(conversation.Messages).Content);
    }

    [Fact] public void EmptyToolOutput_IsFailure()
    {
        var result = ToolResult.Success(1, "test", "tool", new Dictionary<string, object?>(), " ");
        Assert.False(result.Succeeded);
        Assert.True(result.IsEmpty);
    }
}

public sealed class PlannerTests
{
    [Fact] public async Task NoTools_ProducesDirectPlanWithoutCallingModel()
    {
        var model = new FakeChatModel();
        var planner = new ChatModelPlanner(model);
        var plan = await planner.CreatePlanAsync(new PlanningRequest("hello", [], []), default);
        Assert.Equal(PlanKind.DirectAnswer, plan.Kind);
        Assert.Equal(0, model.Completions);
    }

    [Fact] public async Task ModelJson_ProducesMultiStepPlan()
    {
        var model = new FakeChatModel { Completion = """{"goal":"work","kind":"multi_step","steps":[{"id":1,"description":"date","tool":"GetDate","input":{}},{"id":2,"description":"math","tool":"Calculator","input":{"input":"1+1"}}]}""" };
        var plan = await new ChatModelPlanner(model).CreatePlanAsync(new PlanningRequest("work", [], [new ToolDefinition("Calculator", "math", ["input"])]), default);
        Assert.Equal(PlanKind.MultiStep, plan.Kind);
        Assert.Equal(2, plan.Steps.Count);
    }
}

public sealed class ToolRegistryTests
{
    [Fact] public void Selection_RespectsWebAndAttachmentFlags()
    {
        var registry = new ToolRegistry([new StubTool("general"), new StubTool("web", ToolCategory.WebSearch), new StubTool("file", ToolCategory.File)]);
        Assert.Equal(["general"], registry.Select(true, false, false).Select(t => t.Definition.Name));
        Assert.Equal(["file"], registry.Select(false, false, true).Select(t => t.Definition.Name));
    }
}

public sealed class ExecutorTests
{
    [Fact] public async Task MultiStep_ExecutesToolsSequentially()
    {
        var calls = new List<string>();
        var tool1 = new StubTool("GetDate", handler: _ => { calls.Add("date"); return "2026-09-29"; });
        var tool2 = new StubTool("Calculator", handler: _ => { calls.Add("calculator"); return "10"; });
        var registry = new ToolRegistry([tool1, tool2]);
        var executor = new AgentExecutor(registry, new StubPlanner());
        var plan = new Plan { Goal = "two steps", Kind = PlanKind.MultiStep, Steps =
        [
            new PlanStep { Id = 1, Description = "date", Tool = "GetDate" },
            new PlanStep { Id = 2, Description = "calculate", Tool = "Calculator", Input = new Dictionary<string, object?> { ["input"] = "5+5" } }
        ]};
        var result = await executor.ExecuteAsync(plan, [tool1, tool2], plan.Goal, null, default);
        Assert.True(result.Completed);
        Assert.Equal(new[] { "date", "calculator" }, calls);
    }
}

public sealed class InfrastructureToolTests
{
    [Fact] public async Task Calculator_EvaluatesExpressionWithoutExternalService()
    {
        var tool = new Agent.Infrastructure.Tools.Calculator.CalculatorTool();
        var result = await tool.ExecuteAsync(new ToolContext(new Dictionary<string, object?> { ["input"] = "5*10" }, null), default);
        Assert.True(result.Succeeded);
        Assert.Equal("50", result.Output);
    }
}

public sealed class AgentOrchestrationTests
{
    [Fact] public async Task CancelledRequest_StopsBeforePlanning()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var model = new FakeChatModel();
        var registry = new ToolRegistry([]);
        var planner = new ChatModelPlanner(model);
        var agent = new PlanningAgent(model, planner, new AgentExecutor(registry, planner), registry);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in agent.RunAsync(new AgentRequest("hello", false, false), new Conversation(), cancellation.Token)) { }
        });
    }

    [Fact] public async Task Agent_PlansExecutesAndStreamsFinalAnswer()
    {
        var model = new FakeChatModel { Completion = """{"goal":"time","kind":"single_tool","steps":[{"id":1,"description":"time","tool":"GetTime","input":{}}]}""", Streamed = "It is noon" };
        var tool = new StubTool("GetTime", handler: _ => "12:00:00");
        var registry = new ToolRegistry([tool]);
        var planner = new ChatModelPlanner(model);
        var agent = new PlanningAgent(model, planner, new AgentExecutor(registry, planner), registry);
        var chunks = new List<string>();
        await foreach (var chunk in agent.RunAsync(new AgentRequest("time?", true, false), new Conversation(), default)) chunks.Add(chunk);
        Assert.Equal("It is noon", string.Concat(chunks));
        Assert.Contains(model.StreamMessages, m => m.Content.Contains("12:00:00"));
    }
}

file sealed class FakeChatModel : IChatModel
{
    public string Completion { get; set; } = "{}";
    public string Streamed { get; set; } = "answer";
    public int Completions { get; private set; }
    public IReadOnlyList<Message> StreamMessages { get; private set; } = [];
    public Task<string> CompleteAsync(IReadOnlyList<Message> messages, ChatModelOptions options, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Completions++; return Task.FromResult(Completion); }
    public async IAsyncEnumerable<string> StreamAsync(IReadOnlyList<Message> messages, ChatModelOptions options, [EnumeratorCancellation] CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); StreamMessages = messages; yield return Streamed; await Task.CompletedTask; }
}

file sealed class StubTool(string name, ToolCategory category = ToolCategory.General, Func<ToolContext, string>? handler = null) : IAgentTool
{
    public ToolDefinition Definition { get; } = new(name, name, [], category);
    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(ToolResult.Success(0, "", name, context.Arguments, handler?.Invoke(context) ?? "ok")); }
}

file sealed class StubPlanner : IPlanner
{
    public Task<Plan> CreatePlanAsync(PlanningRequest request, CancellationToken cancellationToken) => Task.FromResult(Plan.Direct("direct"));
    public Task<ExecutionDirective> DecideNextAsync(ExecutionState state, CancellationToken cancellationToken) => Task.FromResult(ExecutionDirective.Abort("stop"));
}
