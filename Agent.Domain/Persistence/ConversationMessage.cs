using Agent.Domain.Persistence;

namespace Agent.Domain.Persistence;

public sealed class ConversationMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public int SequenceNumber { get; set; }
    public ConversationMessageRole Role { get; set; }
    public ConversationMessageStatus Status { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? ModelName { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Conversation Conversation { get; set; } = null!;
    public ICollection<ToolExecution> ToolExecutions { get; } = new List<ToolExecution>();
}
