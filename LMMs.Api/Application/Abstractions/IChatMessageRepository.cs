using LMMs.Api.Application.Contracts;
using LMMs.Api.Domain.Entities;

namespace LMMs.Api.Application.Abstractions;

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
