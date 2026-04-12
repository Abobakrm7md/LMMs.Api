using LMMs.Api.Interfaces;
using System.ComponentModel;

namespace LMMs.Api.Services
{
    public class GetTimeTool : IAgentTool
    {
        public string Name => "GetTime";

        public Delegate GetFunction() => (Func<string>)GetTime;

        [Description("Returns the current time from the server. Call ONLY when the user asks for the current time or similar.")]
        private string GetTime()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }
    }
}
