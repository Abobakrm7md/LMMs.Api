using System.ComponentModel.DataAnnotations;

namespace Agent.Api.Contracts;

public sealed class CreateConversationRequest
{
    [StringLength(200)]
    public string? Title { get; init; }
}

public sealed class SendConversationMessageRequest
{
    [Required, StringLength(16_000)]
    public string Prompt { get; init; } = string.Empty;

    public IFormFile? File { get; init; }
}
