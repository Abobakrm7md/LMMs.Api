using LMMs.Api.Interfaces;
using System.ComponentModel;
using System.Data;

namespace LMMs.Api.Services
{
    public class CalculatorTool : IAgentTool
    {
        public string Name => "Calculator";

        public Delegate GetFunction() => (Func<string, string>)Calculate;

        [Description("ONLY use this for mathematical calculations like '5*10'. DO NOT use for text or explanations.")]
        private string Calculate(string input)
        {
            try
            {
                var result = new DataTable().Compute(input, null);
                return result.ToString();
            }
            catch
            {
                return "Invalid calculation";
            }
        }
    }
}
