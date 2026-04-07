using LMMs.Api.Controllers;

namespace LMMs.Api.Interfaces
{
    public interface ILLMSDecision
    {
        Task<AgentDecision> GetDecision(string context);
    }
}
