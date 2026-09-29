using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LMMs.Api.Agents;
using LMMs.Api.Application.Abstractions;
using LMMs.Api.Application.Contracts;
using LMMs.Api.Application.Exceptions;
using LMMs.Api.Domain.Entities;
using LMMs.Api.Domain.Enums;
using LMMs.Api.Planning;

namespace LMMs.Api.Application.Services;

public sealed class ConversationTurnService(
    ICurrentUser currentUser,
    IConversationRepository conversationRepository,
    IChatMessageRepository chatMessageRepository,
    IToolExecutionRepository toolExecutionRepository,
    IUnitOfWork unitOfWork,
    IAgentTurnRunner agent,
    IClock clock) : IConversationTurnService
{
    private const int MaxConversationListSize = 100;
    private const int MaxMessagePageSize = 100;
    private const int MaxPromptCharacters = 16_000;
    private const int MaxAgentHistoryMessages = 20;
    private const int MaxAgentHistoryCharacters = 20_000;
    private const int MaxPersistedToolTextCharacters = 16_000;

    public async Task<ConversationSummaryDto> CreateAsync(CreateConversationCommand command, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var now = clock.UtcNow;
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = NormalizeTitle(command.Title),
            CreatedAt = now,
            UpdatedAt = now,
            NextMessageSequence = 0
        };

        await conversationRepository.AddAsync(conversation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToSummary(conversation);
    }

    public Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(int take, CancellationToken cancellationToken) =>
        conversationRepository.ListOwnedAsync(RequireUserId(), Math.Clamp(take, 1, MaxConversationListSize), cancellationToken);

    public async Task<ConversationDetailDto> GetAsync(
        Guid conversationId,
        int? beforeSequence,
        int take,
        CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var conversation = await GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var messagePage = await chatMessageRepository.GetPageOwnedAsync(
            userId,
            conversationId,
            beforeSequence,
            Math.Clamp(take, 1, MaxMessagePageSize),
            cancellationToken);

        return new ConversationDetailDto(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.UpdatedAt,
            conversation.LastMessageAt,
            messagePage);
    }

    public async Task EnsureOwnedAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        await GetOwnedOrThrowAsync(RequireUserId(), conversationId, cancellationToken);
    }

    public async IAsyncEnumerable<ConversationStreamEvent> SendMessageAsync(
        Guid conversationId,
        SendMessageCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var prompt = NormalizePrompt(command.Prompt);
        var conversation = await GetOwnedOrThrowAsync(userId, conversationId, cancellationToken);
        var now = clock.UtcNow;

        var userMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            SequenceNumber = ++conversation.NextMessageSequence,
            Role = ConversationMessageRole.User,
            Status = ConversationMessageStatus.Completed,
            Content = prompt,
            CreatedAt = now,
            UpdatedAt = now
        };
        var assistantMessage = new ConversationMessage
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            SequenceNumber = ++conversation.NextMessageSequence,
            Role = ConversationMessageRole.Assistant,
            Status = ConversationMessageStatus.Streaming,
            Content = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (string.Equals(conversation.Title, "New chat", StringComparison.Ordinal))
            conversation.Title = TitleFromPrompt(prompt);
        conversation.UpdatedAt = now;
        conversation.LastMessageAt = now;

        await chatMessageRepository.AddRangeAsync([userMessage, assistantMessage], cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        yield return new ConversationStreamEvent("message-start", userMessage.Id, assistantMessage.Id);

        var history = await chatMessageRepository.GetRecentForAgentAsync(
            userId,
            conversationId,
            MaxAgentHistoryMessages,
            MaxAgentHistoryCharacters,
            cancellationToken);
        var turn = new AgentTurnRequest(prompt, history, command.EnableTools, command.EnableWebSearch);
        var response = new StringBuilder();
        var completed = false;

        try
        {
            await foreach (var agentEvent in agent.RunTurnAsync(turn, cancellationToken).WithCancellation(cancellationToken))
            {
                switch (agentEvent)
                {
                    case ToolExecutionCompleted toolExecution:
                        await PersistToolExecutionAsync(assistantMessage.Id, toolExecution.Result, cancellationToken);
                        break;
                    case TextDeltaProduced textDelta when !string.IsNullOrEmpty(textDelta.Text):
                        response.Append(textDelta.Text);
                        yield return new ConversationStreamEvent("delta", AssistantMessageId: assistantMessage.Id, Text: textDelta.Text);
                        break;
                    case AgentTurnCompleted _:
                        break;
                }
            }

            assistantMessage.Content = response.ToString();
            assistantMessage.Status = ConversationMessageStatus.Completed;
            assistantMessage.UpdatedAt = clock.UtcNow;
            conversation.UpdatedAt = assistantMessage.UpdatedAt;
            conversation.LastMessageAt = assistantMessage.UpdatedAt;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            completed = true;
            yield return new ConversationStreamEvent("completed", AssistantMessageId: assistantMessage.Id, Status: "completed");
        }
        finally
        {
            if (!completed)
            {
                assistantMessage.Content = response.ToString();
                assistantMessage.Status = cancellationToken.IsCancellationRequested
                    ? ConversationMessageStatus.Cancelled
                    : ConversationMessageStatus.Failed;
                assistantMessage.UpdatedAt = clock.UtcNow;
                conversation.UpdatedAt = assistantMessage.UpdatedAt;
                conversation.LastMessageAt = assistantMessage.UpdatedAt;
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
            }
        }
    }

    public async Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var deleted = await conversationRepository.DeleteOwnedAsync(RequireUserId(), conversationId, cancellationToken);
        if (!deleted)
            throw new ResourceNotFoundException("Conversation was not found.");

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task PersistToolExecutionAsync(
        Guid assistantMessageId,
        ToolExecutionResult result,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var execution = new ToolExecution
        {
            Id = Guid.NewGuid(),
            AssistantMessageId = assistantMessageId,
            StepNumber = result.StepId,
            ToolName = Truncate(result.ToolName, 128),
            Description = Truncate(result.Description, 500),
            ArgumentsJson = Truncate(JsonSerializer.Serialize(result.Arguments), MaxPersistedToolTextCharacters),
            Output = Truncate(result.Output, MaxPersistedToolTextCharacters),
            Error = Truncate(result.Error, 2_000),
            Succeeded = result.Succeeded,
            StartedAt = now,
            CompletedAt = now
        };

        await toolExecutionRepository.AddAsync(execution, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<Conversation> GetOwnedOrThrowAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        await conversationRepository.GetOwnedAsync(userId, conversationId, cancellationToken)
        ?? throw new ResourceNotFoundException("Conversation was not found.");

    private Guid RequireUserId() => currentUser.UserId
        ?? throw new UnauthorizedAccessException("An authenticated user is required.");

    private static ConversationSummaryDto ToSummary(Conversation conversation) =>
        new(conversation.Id, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt, conversation.LastMessageAt);

    private static string NormalizePrompt(string? value)
    {
        var prompt = value?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
            throw new RequestValidationException("A message is required.");
        if (prompt.Length > MaxPromptCharacters)
            throw new RequestValidationException($"A message cannot exceed {MaxPromptCharacters:N0} characters.");
        return prompt;
    }

    private static string NormalizeTitle(string? title)
    {
        var normalized = title?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "New chat";
        if (normalized.Length > 200)
            throw new RequestValidationException("A conversation title cannot exceed 200 characters.");
        return normalized;
    }

    private static string TitleFromPrompt(string prompt)
    {
        var title = string.Join(' ', prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length <= 80 ? title : title[..77] + "...";
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
}
