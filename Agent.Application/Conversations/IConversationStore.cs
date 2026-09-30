using Agent.Domain.Conversations;
namespace Agent.Application.Conversations;
public interface IConversationStore { Conversation GetOrCreate(string conversationId); }
