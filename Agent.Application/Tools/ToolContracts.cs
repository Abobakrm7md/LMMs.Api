using Agent.Domain.Tools;

namespace Agent.Application.Tools;

public sealed record ToolDefinition(string Name, string Description, IReadOnlyList<string> Parameters, ToolCategory Category = ToolCategory.General);
public enum ToolCategory { General, WebSearch, File }
public sealed record ToolContext(IReadOnlyDictionary<string, object?> Arguments, string? AttachmentId);

public interface IAgentTool
{
    ToolDefinition Definition { get; }
    Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken);
}

public interface IToolRegistry
{
    IReadOnlyList<IAgentTool> Select(bool enableTools, bool enableWebSearch, bool hasAttachment);
    IAgentTool? Find(string name, IReadOnlyList<IAgentTool> availableTools);
}
