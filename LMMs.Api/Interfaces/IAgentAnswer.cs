using Microsoft.Extensions.AI;

namespace LMMs.Api.Interfaces
{
    public interface IAgentAnswer
    {
        Task<string> RunAgent(string userPrompt, List<ChatMessage> chatMessages, HttpResponse httpResponse, CancellationToken cancellationToken);
        IAsyncEnumerable<string> RunAgentUsingAIFunctions(string userPrompt, List<ChatMessage> chatMessages, CancellationToken cancellationToken);
    }
}
