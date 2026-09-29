namespace LMMs.Api.Domain.Enums;

public enum ConversationMessageStatus : byte
{
    Streaming = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}
