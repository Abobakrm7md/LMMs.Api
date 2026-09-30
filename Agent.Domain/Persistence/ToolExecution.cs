namespace Agent.Domain.Persistence;

public sealed class ToolExecution
{
    public Guid Id { get; set; }
    public Guid AssistantMessageId { get; set; }
    public int StepNumber { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ArgumentsJson { get; set; }
    public string? Output { get; set; }
    public string? Error { get; set; }
    public bool Succeeded { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }

    public ConversationMessage AssistantMessage { get; set; } = null!;
}
