using LMMs.Api.Agents;
using LMMs.Api.Interfaces;
using LMMs.Api.ViewModels;
using Microsoft.Extensions.AI;

namespace LMMs.Api.Services
{
    public class AgentAnswer : IAgentAnswer
    {
        private readonly IAgent _agent;

        public AgentAnswer(IAgent agent)
        {
            _agent = agent;
        }

        public IAsyncEnumerable<string> RunAgentUsingAIFunctions(
            ChatRequest request,
            List<ChatMessage> chatMessages,
            CancellationToken cancellationToken) =>
            _agent.RunAsync(request, chatMessages, cancellationToken);
    }
}
