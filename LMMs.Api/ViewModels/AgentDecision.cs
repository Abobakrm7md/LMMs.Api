namespace LMMs.Api.ViewModels
{
    public class AgentDecision
    {
        public string action { get; set; }
        public string tool { get; set; }
        public string input { get; set; }
        public string answer { get; set; }
    }
}
