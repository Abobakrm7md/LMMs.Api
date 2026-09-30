using System.ComponentModel.DataAnnotations;

namespace Agent.Api.Contracts;

public sealed class ChatRequest
{
    [Required]
    public string Prompt { get; set; } = string.Empty;

    public IFormFile? File { get; set; }
    public string? ConversationId { get; set; }
}
