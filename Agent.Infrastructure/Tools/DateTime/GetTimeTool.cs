using Agent.Application.Tools;
using Agent.Domain.Tools;
namespace Agent.Infrastructure.Tools.DateTime;
public sealed class GetTimeTool(TimeProvider timeProvider) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("GetTime", "Returns the current server time.", []);
    public Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = timeProvider.GetLocalNow().ToString("HH:mm:ss");
        return Task.FromResult(ToolResult.Success(0, "", Definition.Name, context.Arguments, output));
    }
}
