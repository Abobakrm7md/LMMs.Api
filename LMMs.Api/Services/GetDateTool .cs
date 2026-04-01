using LMMs.Api.Interfaces;
using System.ComponentModel;

namespace LMMs.Api.Services
{
    public class GetDateTool : IAgentTool
    {
        public string Name => "GetDate";

        public Delegate GetFunction() => (Func<string>)GetDate;
        [Description("Returns today's date as a string in format yyyy-MM-dd. This is the final answer to show to the user.")]
        private string GetDate()
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }
    }
}
