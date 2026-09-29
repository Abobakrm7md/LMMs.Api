using Agent.Domain.Persistence;

namespace Agent.Application.Conversations;

public sealed record CreateConversationCommand(string? Title);
public sealed record SendMessageCommand(string Prompt, bool EnableTools, bool EnableWebSearch);

public sealed record ConversationSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastMessageAt);

public sealed record MessageDto(
    Guid Id,
    int SequenceNumber,
    ConversationMessageRole Role,
    ConversationMessageStatus Status,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ModelName);

public sealed record MessagePageDto(
    IReadOnlyList<MessageDto> Messages,
    int? NextBeforeSequence);

public sealed record ConversationDetailDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastMessageAt,
    MessagePageDto MessagePage);

public sealed record ConversationStreamEvent(
    string Type,
    Guid? UserMessageId = null,
    Guid? AssistantMessageId = null,
    string? Text = null,
    string? Status = null);

public enum AgentHistoryRole
{
    User,
    Assistant
}

public sealed record AgentHistoryMessage(AgentHistoryRole Role, string Content);
