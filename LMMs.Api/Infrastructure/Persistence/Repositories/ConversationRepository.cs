using LMMs.Api.Application.Abstractions;
using LMMs.Api.Application.Contracts;
using LMMs.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LMMs.Api.Infrastructure.Persistence.Repositories;

public sealed class ConversationRepository(AppDbContext dbContext) : IConversationRepository
{
    public Task AddAsync(Conversation conversation, CancellationToken cancellationToken) =>
        dbContext.Conversations.AddAsync(conversation, cancellationToken).AsTask();

    public Task<Conversation?> GetOwnedAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken) =>
        dbContext.Conversations.SingleOrDefaultAsync(
            conversation => conversation.Id == conversationId && conversation.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyList<ConversationSummaryDto>> ListOwnedAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken)
    {
        return await dbContext.Conversations
            .AsNoTracking()
            .Where(conversation => conversation.UserId == userId)
            .OrderByDescending(conversation => conversation.UpdatedAt)
            .ThenByDescending(conversation => conversation.Id)
            .Take(take)
            .Select(conversation => new ConversationSummaryDto(
                conversation.Id,
                conversation.Title,
                conversation.CreatedAt,
                conversation.UpdatedAt,
                conversation.LastMessageAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteOwnedAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await GetOwnedAsync(userId, conversationId, cancellationToken);
        if (conversation is null)
            return false;

        dbContext.Conversations.Remove(conversation);
        return true;
    }
}
