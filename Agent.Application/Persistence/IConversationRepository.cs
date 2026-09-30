using Agent.Application.Conversations;
using Agent.Domain.Persistence;

namespace Agent.Application.Persistence;

public interface IConversationRepository
{
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
    Task<Conversation?> GetOwnedAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationSummaryDto>> ListOwnedAsync(Guid userId, int take, CancellationToken cancellationToken);
    Task<bool> DeleteOwnedAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken);
}
