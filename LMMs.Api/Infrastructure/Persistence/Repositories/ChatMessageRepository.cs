using LMMs.Api.Application.Abstractions;
using LMMs.Api.Application.Contracts;
using LMMs.Api.Domain.Entities;
using LMMs.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LMMs.Api.Infrastructure.Persistence.Repositories;

public sealed class ChatMessageRepository(AppDbContext dbContext) : IChatMessageRepository
{
    public Task AddRangeAsync(IEnumerable<ConversationMessage> messages, CancellationToken cancellationToken) =>
        dbContext.ChatMessages.AddRangeAsync(messages, cancellationToken);

    public async Task<IReadOnlyList<AgentHistoryMessage>> GetRecentForAgentAsync(
        Guid userId,
        Guid conversationId,
        int maxMessages,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        var newestFirst = await dbContext.ChatMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId
                              && message.Conversation.UserId == userId
                              && (message.Role == ConversationMessageRole.User
                                  || message.Status == ConversationMessageStatus.Completed))
            .OrderByDescending(message => message.SequenceNumber)
            .Take(maxMessages)
            .Select(message => new { message.Role, message.Content })
            .ToListAsync(cancellationToken);

        var history = new List<AgentHistoryMessage>(newestFirst.Count);
        var usedCharacters = 0;
        foreach (var message in newestFirst.AsEnumerable().Reverse())
        {
            var remaining = maxCharacters - usedCharacters;
            if (remaining <= 0)
                break;

            var content = message.Content.Length <= remaining
                ? message.Content
                : message.Content[..remaining];
            usedCharacters += content.Length;
            history.Add(new AgentHistoryMessage(
                message.Role == ConversationMessageRole.Assistant ? AgentHistoryRole.Assistant : AgentHistoryRole.User,
                content));
        }

        return history;
    }

    public async Task<MessagePageDto> GetPageOwnedAsync(
        Guid userId,
        Guid conversationId,
        int? beforeSequence,
        int take,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ChatMessages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId && message.Conversation.UserId == userId);

        if (beforeSequence.HasValue)
            query = query.Where(message => message.SequenceNumber < beforeSequence.Value);

        var newestFirst = await query
            .OrderByDescending(message => message.SequenceNumber)
            .Take(take + 1)
            .Select(message => new MessageDto(
                message.Id,
                message.SequenceNumber,
                message.Role,
                message.Status,
                message.Content,
                message.CreatedAt,
                message.UpdatedAt,
                message.ModelName))
            .ToListAsync(cancellationToken);

        var hasMore = newestFirst.Count > take;
        if (hasMore)
            newestFirst.RemoveAt(newestFirst.Count - 1);

        newestFirst.Reverse();
        var nextBeforeSequence = hasMore && newestFirst.Count > 0
            ? newestFirst[0].SequenceNumber
            : null;
        return new MessagePageDto(newestFirst, nextBeforeSequence);
    }
}
