using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using System.Threading;

namespace YourNamespace.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IChatClient _chatClient;

        // Store chat messages for the entire session (simple demo)
        private static readonly List<ChatMessage> _chatMessages = new();

        public ChatController(IChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        [HttpPost("/api/chat")]
        public async Task Stream([FromBody] ChatRequest request)
        {
            Response.ContentType = "text/event-stream";
            var cancellationToken = HttpContext.RequestAborted;

            // 1️⃣ Add user message
            _chatMessages.Add(new ChatMessage(ChatRole.User, request.Prompt));

            string assistantResponse = string.Empty;

            // 2️⃣ Stream Ollama response
            await foreach (var item in _chatClient.GetStreamingResponseAsync(
                _chatMessages, cancellationToken: cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var text = item.Text ?? string.Empty;
                assistantResponse += text;

                await Response.WriteAsync(text);
                await Response.Body.FlushAsync();
            }

            // 3️⃣ After streaming completes, save assistant’s reply
            if (!string.IsNullOrWhiteSpace(assistantResponse))
            {
                _chatMessages.Add(new ChatMessage(ChatRole.Assistant, assistantResponse));
            }
        }
    }

    public class ChatRequest
    {
        public string Prompt { get; set; } = string.Empty;
    }
}
