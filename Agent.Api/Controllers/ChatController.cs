using Agent.Api.Contracts;
using Agent.Application.Agents;
using Agent.Application.Conversations;
using Agent.Application.Files;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ChatController(IAgent agent, IAttachmentStore attachments, IConversationStore conversations) : ControllerBase
{
    [HttpPost("/api/chat")]
    [Consumes("multipart/form-data")]
    public async Task Stream([FromForm] ChatRequest request)
    {
        var cancellationToken = HttpContext.RequestAborted;
        StoredAttachment? attachment = null;
        if (request.File is not null)
        {
            await using var stream = request.File.OpenReadStream();
            attachment = await attachments.SaveAsync(request.File.FileName, stream, cancellationToken);
        }

        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Request.Headers["X-Conversation-Id"].FirstOrDefault() ?? "default"
            : request.ConversationId;
        var conversation = conversations.GetOrCreate(conversationId);
        var command = new AgentRequest(request.Prompt, attachment?.Id, attachment?.OriginalName);

        Response.ContentType = "text/event-stream";
        await foreach (var piece in agent.RunAsync(command, conversation, cancellationToken).WithCancellation(cancellationToken))
        {
            await Response.WriteAsync(piece, cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }
}
