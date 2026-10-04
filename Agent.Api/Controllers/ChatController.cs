using System.Text.Json;
using Agent.Api.Contracts;
using Agent.Application.Common;
using Agent.Application.Conversations;
using Agent.Application.Files;
using Agent.Application.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/chat")]
public sealed class ChatController(
    IConversationTurnService conversations,
    IAttachmentStore attachments) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [Produces("text/event-stream")]
    public async Task Stream([FromForm] ChatRequest request, CancellationToken cancellationToken)
    {
        Guid conversationId;
        if (!Guid.TryParse(request.ConversationId ?? Request.Headers["X-Conversation-Id"].FirstOrDefault(), out conversationId))
        {
            var created = await conversations.CreateAsync(new CreateConversationCommand(null), cancellationToken);
            conversationId = created.Id;
            Response.Headers["X-Conversation-Id"] = conversationId.ToString();
        }

        try
        {
            await conversations.EnsureOwnedAsync(conversationId, cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        StoredAttachment? attachment = null;
        if (request.File is not null)
        {
            await using var stream = request.File.OpenReadStream();
            attachment = await attachments.SaveAsync(request.File.FileName, stream, cancellationToken);
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        try
        {
            await foreach (var item in conversations.SendMessageAsync(
                               conversationId,
                               new SendMessageCommand(request.Prompt, attachment?.Id, attachment?.OriginalName),
                               cancellationToken))
            {
                var json = JsonSerializer.Serialize(item);
                await Response.WriteAsync($"event: {item.Type}\ndata: {json}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (RequestValidationException exception)
        {
            await Response.WriteAsync($"event: error\ndata: {JsonSerializer.Serialize(new { error = exception.Message })}\n\n", CancellationToken.None);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
        }
    }
}
