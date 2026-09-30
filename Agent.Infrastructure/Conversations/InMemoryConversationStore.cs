using System.Collections.Concurrent;
using Agent.Application.Conversations;
using Agent.Domain.Conversations;
namespace Agent.Infrastructure.Conversations;
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, Conversation> _items = new();
    public Conversation GetOrCreate(string conversationId) => _items.GetOrAdd(conversationId, _ => new Conversation());
}
