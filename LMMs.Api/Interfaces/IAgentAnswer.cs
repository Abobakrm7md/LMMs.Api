using Microsoft.Extensions.AI;
using YourNamespace.Controllers;

namespace LMMs.Api.Interfaces
{
    public interface IAgentAnswer
    {
        Task<string> RunAgent(string userPrompt, List<ChatMessage> chatMessages, HttpResponse httpResponse, CancellationToken cancellationToken);
        IAsyncEnumerable<string> RunAgentUsingAIFunctions(ChatRequest request, List<ChatMessage> chatMessages, CancellationToken cancellationToken);
    }
}
