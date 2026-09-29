using LMMs.Api.Application.Contracts;
using LMMs.Api.Domain.Entities;

namespace LMMs.Api.Application.Abstractions;

public interface IConversationRepository
{
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
    Task<Conversation?> GetOwnedAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationSummaryDto>> ListOwnedAsync(Guid userId, int take, CancellationToken cancellationToken);
    Task<bool> DeleteOwnedAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
}
