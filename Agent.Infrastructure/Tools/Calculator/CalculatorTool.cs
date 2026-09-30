using System.Data;
using Agent.Application.Tools;
using Agent.Domain.Tools;
namespace Agent.Infrastructure.Tools.Calculator;

public sealed class CalculatorTool : IAgentTool
{
    public ToolDefinition Definition { get; } = new("Calculator", "Use only for mathematical calculations such as 5*10.", ["input"]);
    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = Convert.ToString(context.Arguments.GetValueOrDefault("input")) ?? string.Empty;
        try
        {
            var value = new DataTable().Compute(input.Replace("×", "*").Replace("÷", "/").Replace("−", "-"), null)?.ToString() ?? string.Empty;
            return Task.FromResult(ToolResult.Success(0, string.Empty, Definition.Name, context.Arguments, value));
        }
        catch { return Task.FromResult(ToolResult.Failure(0, string.Empty, Definition.Name, context.Arguments, "Invalid calculation", "Invalid calculation")); }
    }
}
