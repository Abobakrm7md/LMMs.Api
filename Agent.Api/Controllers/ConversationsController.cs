using System.Text.Json;
using Agent.Application.Persistence;
using Agent.Application.Conversations;
using Agent.Application.Files;
using Agent.Application.Common;
using Agent.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/conversations")]
public sealed class ConversationsController(
    IConversationTurnService conversationService,
    IAttachmentStore attachmentStore) : ControllerBase
{
    private const long MaximumAttachmentBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> SupportedAttachmentExtensions =
        [".txt", ".csv", ".json", ".xml", ".md", ".pdf", ".docx"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost]
    [ProducesResponseType(typeof(ConversationSummaryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<ConversationSummaryDto>> Create(
        CreateConversationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var conversation = await conversationService.CreateAsync(
                new CreateConversationCommand(request.Title), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = conversation.Id }, conversation);
        }
        catch (RequestValidationException exception)
        {
            return ValidationProblem(detail: exception.Message);
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ConversationSummaryDto>), StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ConversationSummaryDto>> List([FromQuery] int take = 30, CancellationToken cancellationToken = default) =>
        conversationService.ListAsync(take, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ConversationDetailDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ConversationDetailDto>> Get(
        Guid id,
        [FromQuery] int? beforeSequence,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return Ok(await conversationService.GetAsync(id, beforeSequence, take, cancellationToken));
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpGet("{id:guid}/messages")]
    [ProducesResponseType(typeof(MessagePageDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MessagePageDto>> GetMessages(
        Guid id,
        [FromQuery] int? beforeSequence,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var conversation = await conversationService.GetAsync(id, beforeSequence, take, cancellationToken);
            return Ok(conversation.MessagePage);
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/messages")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    [Produces("text/event-stream")]
    public async Task SendMessage(
        Guid id,
        [FromForm] SendConversationMessageRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate ownership before starting a streaming response, so a foreign ID returns a real 404.
            await conversationService.EnsureOwnedAsync(id, cancellationToken);
        }
        catch (ResourceNotFoundException)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        StoredAttachment? attachment = null;
        if (request.File is not null)
        {
            var extension = Path.GetExtension(request.File.FileName).ToLowerInvariant();
            if (request.File.Length is <= 0 or > MaximumAttachmentBytes ||
                !SupportedAttachmentExtensions.Contains(extension))
            {
                Response.StatusCode = StatusCodes.Status400BadRequest;
                await Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "Invalid attachment",
                    Detail = "Upload a non-empty txt, csv, json, xml, md, pdf, or docx file no larger than 10 MB.",
                    Status = StatusCodes.Status400BadRequest
                }, cancellationToken);
                return;
            }

            await using var fileStream = request.File.OpenReadStream();
            attachment = await attachmentStore.SaveAsync(request.File.FileName, fileStream, cancellationToken);
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var item in conversationService.SendMessageAsync(
                               id,
                               new SendMessageCommand(request.Prompt, attachment?.Id, attachment?.OriginalName),
                               cancellationToken))
            {
                await WriteEventAsync(item, cancellationToken);
            }
        }
        catch (RequestValidationException exception)
        {
            await WriteEventAsync(new ConversationStreamEvent("error", Text: exception.Message), CancellationToken.None);
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            // The conversation service marks the assistant placeholder as cancelled in its finally block.
        }
        catch
        {
            await WriteEventAsync(new ConversationStreamEvent("error", Text: "The assistant could not complete this response."), CancellationToken.None);
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await conversationService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (ResourceNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task WriteEventAsync(ConversationStreamEvent item, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(item, JsonOptions);
        await Response.WriteAsync($"event: {item.Type}\ndata: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
