using LMMs.Api.ViewModels;

namespace LMMs.Api.Interfaces
{
    public interface ILLMSDecision
    {
        Task<AgentDecision> GetDecision(string context);
    }
}
