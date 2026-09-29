using System.ComponentModel;
using System.Data;
using LMMs.Api.Interfaces;
using LMMs.Api.Planning;

namespace LMMs.Api.Services;

public sealed class CalculatorTool : IAgentTool
{
    public string Name => "Calculator";
    public Delegate GetFunction() => (Func<string, string>)Calculate;

    [Description("ONLY use this for mathematical calculations like '5*10'. DO NOT use for text or explanations.")]
    private string Calculate(string input)
    {
        try
        {
            var expression = NormalizeExpression(input);
            if (!PlanStepResolver.IsValidMathExpression(expression))
                return "Invalid calculation";

            var result = new DataTable().Compute(expression, null);
            return result?.ToString() ?? "Invalid calculation";
        }
        catch
        {
            return "Invalid calculation";
        }
    }

    private static string NormalizeExpression(string input) =>
        input.Replace("×", "*").Replace("÷", "/").Replace("−", "-");
}
