using Agent.Application.Conversations;

namespace Agent.Application.Persistence;

public interface IConversationTurnService
{
    Task<ConversationSummaryDto> CreateAsync(CreateConversationCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(int take, CancellationToken cancellationToken);
    Task<ConversationDetailDto> GetAsync(Guid conversationId, int? beforeSequence, int take, CancellationToken cancellationToken);
    Task EnsureOwnedAsync(Guid conversationId, CancellationToken cancellationToken);
    IAsyncEnumerable<ConversationStreamEvent> SendMessageAsync(
        Guid conversationId,
        SendMessageCommand command,
        CancellationToken cancellationToken);
    Task DeleteAsync(Guid conversationId, CancellationToken cancellationToken);
}
