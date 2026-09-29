namespace Agent.Domain.Conversations;

public enum MessageRole { System, User, Assistant, Tool }

public sealed record Message(MessageRole Role, string Content);

public sealed class Conversation
{
    private readonly List<Message> _messages = [];
    public IReadOnlyList<Message> Messages => _messages;
    public void Add(Message message) => _messages.Add(message);
    public void TrimToLast(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_messages.Count > count) _messages.RemoveRange(0, _messages.Count - count);
    }
}
