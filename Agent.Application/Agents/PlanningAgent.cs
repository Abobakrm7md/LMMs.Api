using System.Runtime.CompilerServices;
using System.Text;
using Agent.Application.Chat;
using Agent.Application.Execution;
using Agent.Application.Planning;
using Agent.Application.Tools;
using Agent.Domain.Conversations;
using Agent.Domain.Planning;
using Agent.Domain.Tools;

namespace Agent.Application.Agents;

public sealed class PlanningAgent(IChatModel chatModel, IPlanner planner, IAgentExecutor executor, IToolRegistry tools)
    : IAgent, IAgentTurnRunner
{
    public string Name => "PlanningAgent";

    public async IAsyncEnumerable<string> RunAsync(
        AgentRequest request,
        Conversation conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var prompt = request.AttachmentName is null
            ? request.Prompt
            : $"{request.Prompt}\n[An optional attachment is available: {request.AttachmentName}. Call read_file only if answering requires its contents.]";
        conversation.Add(new Message(MessageRole.User, prompt));
        var response = new StringBuilder();

        await foreach (var agentEvent in ExecuteTurnAsync(
                           request with { Prompt = prompt }, conversation, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            if (agentEvent is not TextDeltaProduced delta || string.IsNullOrEmpty(delta.Text))
                continue;
            response.Append(delta.Text);
            yield return delta.Text;
        }

        conversation.Add(new Message(MessageRole.Assistant, response.ToString()));
        conversation.TrimToLast(20);
    }

    public async IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var conversation = new Conversation();
        foreach (var item in request.History)
            conversation.Add(new Message(item.Role == AgentHistoryRole.Assistant ? MessageRole.Assistant : MessageRole.User, item.Content));

        if (conversation.Messages.LastOrDefault() is not { Role: MessageRole.User } last ||
            !string.Equals(last.Content, request.Prompt, StringComparison.Ordinal))
            conversation.Add(new Message(MessageRole.User, request.Prompt));

        var command = new AgentRequest(request.Prompt, request.EnableTools, request.EnableWebSearch);
        await foreach (var item in ExecuteTurnAsync(command, conversation, cancellationToken).WithCancellation(cancellationToken))
            yield return item;
    }

    private async IAsyncEnumerable<AgentTurnEvent> ExecuteTurnAsync(
        AgentRequest request,
        Conversation conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selectedTools = tools.Select(request.EnableTools, request.EnableWebSearch, request.AttachmentId is not null);
        var plan = await planner.CreatePlanAsync(new PlanningRequest(
            request.Prompt,
            conversation.Messages.TakeLast(6).Select(m => $"{m.Role}: {Truncate(m.Content, 200)}").ToList(),
            selectedTools.Select(t => t.Definition).ToList()), cancellationToken);

        ExecutionResult? execution = null;
        if (plan.Kind != PlanKind.DirectAnswer && plan.Steps.Count > 0 && selectedTools.Count > 0)
        {
            execution = await executor.ExecuteAsync(plan, selectedTools, request.Prompt, request.AttachmentId, cancellationToken);
            foreach (var result in execution.StepResults)
                yield return new ToolExecutionCompleted(result);
        }

        var messages = BuildMessages(conversation, plan, execution, selectedTools.Count > 0);
        await foreach (var text in chatModel.StreamAsync(messages, new ChatModelOptions(), cancellationToken).WithCancellation(cancellationToken))
            if (!string.IsNullOrEmpty(text)) yield return new TextDeltaProduced(text);

        yield return new AgentTurnCompleted();
    }

    private static IReadOnlyList<Message> BuildMessages(Conversation conversation, Plan plan, ExecutionResult? execution, bool toolsAvailable)
    {
        var messages = new List<Message> { new(MessageRole.System, SystemPrompt(execution, toolsAvailable)) };
        var used = 0;
        var selected = new List<Message>();
        foreach (var message in conversation.Messages.Reverse())
        {
            var estimate = message.Content.Length / 4 + 4;
            if (used + estimate > 5000) break;
            selected.Insert(0, message);
            used += estimate;
        }
        messages.AddRange(selected);
        if (execution?.StepResults.Count > 0) messages.Add(new Message(MessageRole.User, FormatExecution(plan, execution)));
        return messages;
    }

    private static string SystemPrompt(ExecutionResult? execution, bool toolsAvailable) => execution?.StepResults.Count > 0
        ? "Answer using the request, conversation, and supplied execution results. Do not show internal JSON or plans. Do not invent missing facts; acknowledge failed tools."
        : toolsAvailable
            ? "Answer from the conversation. Tools were available but not needed. Do not invent tool results or mention an internal plan."
            : "Answer from model knowledge and the user's text. No tools are available; do not pretend to call tools or search.";

    private static string FormatExecution(Plan plan, ExecutionResult execution)
    {
        var text = new StringBuilder($"Execution context (not chain-of-thought):\nGoal: {plan.Goal}\nStatus: {(execution.Aborted ? "aborted" : execution.Completed ? "completed" : "partial")}\n");
        if (!string.IsNullOrWhiteSpace(execution.StatusDetail)) text.AppendLine($"Detail: {execution.StatusDetail}");
        foreach (var step in execution.StepResults)
            text.AppendLine($"Step {step.StepId}: {step.Description}\n  tool: {step.ToolName}\n  succeeded: {step.Succeeded}\n  {(step.Succeeded ? "result" : "error")}: {Truncate(step.Succeeded ? step.Output : step.Error, 1500)}");
        return text.ToString();
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? "" : value[..max] + "...";
}
