using Agent.Application.Conversations;
using Agent.Domain.Persistence;

namespace Agent.Application.Persistence;

public interface IChatMessageRepository
{
    Task AddRangeAsync(IEnumerable<ConversationMessage> messages, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentHistoryMessage>> GetRecentForAgentAsync(
        Guid userId,
        Guid conversationId,
        int maxMessages,
        int maxCharacters,
        CancellationToken cancellationToken);
    Task<MessagePageDto> GetPageOwnedAsync(
        Guid userId,
        Guid conversationId,
        int? beforeSequence,
        int take,
        CancellationToken cancellationToken);
}
