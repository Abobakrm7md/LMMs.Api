using Agent.Application.Tools;
using Agent.Domain.Tools;
namespace Agent.Infrastructure.Tools.DateTime;
public sealed class GetDateTool(TimeProvider timeProvider) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("GetDate", "Returns today's date in yyyy-MM-dd format.", []);
    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = timeProvider.GetLocalNow().ToString("yyyy-MM-dd");
        return Task.FromResult(ToolResult.Success(0, "", Definition.Name, context.Arguments, output));
    }
}
