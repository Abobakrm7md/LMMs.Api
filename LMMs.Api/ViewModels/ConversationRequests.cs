using System.ComponentModel.DataAnnotations;

namespace LMMs.Api.ViewModels;

public sealed class CreateConversationRequest
{
    [StringLength(200)]
    public string? Title { get; init; }
}

public sealed class SendConversationMessageRequest
{
    [Required, StringLength(16_000)]
    public string Prompt { get; init; } = string.Empty;

    public bool EnableTools { get; init; }
    public bool EnableWebSearch { get; init; }
}
