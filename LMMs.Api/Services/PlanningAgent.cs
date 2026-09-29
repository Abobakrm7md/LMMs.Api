using System.Runtime.CompilerServices;
using System.Text;
using LMMs.Api.Agents;
using LMMs.Api.Application.Contracts;
using LMMs.Api.Interfaces;
using LMMs.Api.Planning;
using LMMs.Api.ViewModels;
using Microsoft.Extensions.AI;

namespace LMMs.Api.Services;

public sealed class PlanningAgent : IAgent, IAgentTurnRunner
{
    private readonly IChatClient _chatClient;
    private readonly IPlanner _planner;
    private readonly IPlanExecutor _executor;
    private readonly IReadOnlyList<IAgentTool> _tools;
    private readonly FileContext _fileContext;
    private readonly ILogger<PlanningAgent> _logger;

    public PlanningAgent(
        IChatClient chatClient,
        IPlanner planner,
        IPlanExecutor executor,
        IEnumerable<IAgentTool> tools,
        FileContext fileContext,
        ILogger<PlanningAgent> logger)
    {
        _chatClient = chatClient;
        _planner = planner;
        _executor = executor;
        _tools = tools.ToList();
        _fileContext = fileContext;
        _logger = logger;
    }

    public string Name => "PlanningAgent";

    // Compatibility adapter for the original endpoint/tests. Persisted turns use RunTurnAsync instead.
    public async IAsyncEnumerable<string> RunAsync(
        ChatRequest request,
        List<ChatMessage> conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request.File is not null)
        {
            request.Prompt =
                $"{request.Prompt}\n[An optional attachment is available: {request.File.FileName}. Call read_file only if answering requires the file contents.]";
            await SetFileAsync(request.File, cancellationToken);
        }

        conversation.Add(new ChatMessage(ChatRole.User, request.Prompt));
        var history = conversation.Select(ToHistoryMessage).ToList();
        var turn = new AgentTurnRequest(request.Prompt, history, request.EnableTools, request.EnableWebSearch);
        var fullResponse = new StringBuilder();

        await foreach (var agentEvent in RunTurnAsync(turn, cancellationToken).WithCancellation(cancellationToken))
        {
            if (agentEvent is not TextDeltaProduced delta || string.IsNullOrEmpty(delta.Text))
                continue;

            fullResponse.Append(delta.Text);
            yield return delta.Text;
        }

        conversation.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
        TrimHistory(conversation, 20);
    }

    public async IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selectedTools = AgentToolSelector.Select(_tools, new ChatRequest
        {
            Prompt = request.Prompt,
            EnableTools = request.EnableTools,
            EnableWebSearch = request.EnableWebSearch
        });

        var plan = await _planner.CreatePlanAsync(new PlanningRequest
        {
            UserPrompt = request.Prompt,
            RecentConversation = request.History
                .TakeLast(6)
                .Select(message => $"{message.Role}: {Truncate(message.Content, 200)}")
                .ToList(),
            Tools = AgentToolSelector.Describe(selectedTools)
        }, cancellationToken);

        PlanExecutionResult? execution = null;
        if (plan.Kind != PlanKind.DirectAnswer && plan.Steps.Count > 0 && selectedTools.Count > 0)
        {
            execution = await _executor.ExecuteAsync(plan, selectedTools, request.Prompt, cancellationToken);
            foreach (var result in execution.StepResults)
                yield return new ToolExecutionCompleted(result);
        }
        else
        {
            _logger.LogInformation("No tool execution required for this turn");
        }

        var messages = BuildMessagesToSend(request.History, plan, execution, selectedTools.Count > 0);
        var options = new ChatOptions
        {
            Tools = [],
            ToolMode = ChatToolMode.None,
            Temperature = 0.1f
        };

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return new TextDeltaProduced(update.Text);
        }

        _logger.LogInformation("Agent turn completed; planKind={Kind}", plan.Kind);
        yield return new AgentTurnCompleted();
    }

    private static List<ChatMessage> BuildMessagesToSend(
        IReadOnlyList<AgentHistoryMessage> history,
        AgentPlan plan,
        PlanExecutionResult? execution,
        bool toolsWereAvailable)
    {
        var result = new List<ChatMessage>
        {
            new(ChatRole.System, GetSystemPrompt(plan, execution, toolsWereAvailable))
        };

        const int budget = 5000;
        var used = 0;
        var selected = new List<ChatMessage>();
        foreach (var message in history.Reverse())
        {
            var chatMessage = new ChatMessage(
                message.Role == AgentHistoryRole.Assistant ? ChatRole.Assistant : ChatRole.User,
                message.Content);
            var tokens = EstimateTokens(chatMessage);
            if (used + tokens > budget)
                break;
            selected.Insert(0, chatMessage);
            used += tokens;
        }

        result.AddRange(selected);
        if (execution is not null && execution.StepResults.Count > 0)
            result.Add(new ChatMessage(ChatRole.User, FormatExecutionContext(plan, execution)));

        return result;
    }

    private static string GetSystemPrompt(AgentPlan plan, PlanExecutionResult? execution, bool toolsWereAvailable)
    {
        if (execution is null || execution.StepResults.Count == 0)
        {
            return toolsWereAvailable
                ? """
                  You are a helpful assistant.
                  Tools were available but were not required for this request, or planning chose a direct answer.
                  Answer from the conversation. Do not invent tool results. Do not mention an internal plan.
                  """
                : """
                  You are a helpful assistant.
                  For this message, no tools are available. Answer from your knowledge and the text the user sent.
                  Do not pretend to call tools or search the web.
                  """;
        }

        return """
            You are a helpful assistant.
            Answer using the original user request, conversation context, the execution plan, and the tool results.
            Do not show tool calls, JSON, or the plan unless the user asks how you got the answer.
            If a tool failed, say you could not complete that part. Do not invent missing facts.
            """;
    }

    private static string FormatExecutionContext(AgentPlan plan, PlanExecutionResult execution)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Execution context (not chain-of-thought):");
        sb.AppendLine($"Goal: {plan.Goal}");
        sb.AppendLine($"Status: {(execution.Aborted ? "aborted" : execution.Completed ? "completed" : "partial")}");
        if (!string.IsNullOrWhiteSpace(execution.StatusDetail))
            sb.AppendLine($"Detail: {execution.StatusDetail}");

        foreach (var step in execution.StepResults)
        {
            sb.AppendLine($"Step {step.StepId}: {step.Description}");
            sb.AppendLine($"  tool: {step.ToolName}");
            sb.AppendLine($"  succeeded: {step.Succeeded}");
            if (step.Succeeded)
                sb.AppendLine($"  result: {Truncate(step.Output, 1500)}");
            else
                sb.AppendLine($"  error: {step.Error}");
        }

        return sb.ToString();
    }

    private async Task SetFileAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var filesFolder = Path.Combine(Directory.GetCurrentDirectory(), "Files");
        Directory.CreateDirectory(filesFolder);
        var savedFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
        var savePath = Path.Combine(filesFolder, savedFileName);

        await using var stream = File.Create(savePath);
        await file.CopyToAsync(stream, cancellationToken);
        _fileContext.FileName = savedFileName;
    }

    private static AgentHistoryMessage ToHistoryMessage(ChatMessage message) =>
        new(message.Role == ChatRole.Assistant ? AgentHistoryRole.Assistant : AgentHistoryRole.User, message.Text ?? string.Empty);

    private static int EstimateTokens(ChatMessage message) =>
        message.Contents.OfType<TextContent>().Sum(content => content.Text?.Length ?? 0) / 4 + 4;

    private static void TrimHistory(List<ChatMessage> messages, int maxMessages)
    {
        if (messages.Count > maxMessages)
            messages.RemoveRange(0, messages.Count - maxMessages);
    }

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? string.Empty : value[..max] + "...";
}
