using LMMs.Api.ViewModels;
using Microsoft.Extensions.AI;

namespace LMMs.Api.Agents
{
    /// <summary>
    /// Application-facing agent. Later a supervisor can host multiple IAgent implementations.
    /// </summary>
    public interface IAgent
    {
        string Name { get; }

        IAsyncEnumerable<string> RunAsync(
            ChatRequest request,
            List<ChatMessage> conversation,
            CancellationToken cancellationToken);
    }
}
