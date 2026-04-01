using LMMs.Api.Interfaces;

namespace LMMs.Api.Services
{
    public class GetTimeTool : IAgentTool
    {
        public string Name => "GetTime";

        public Delegate GetFunction() => (Func<string>)GetTime;

        private string GetTime()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }
    }
}
