using Agent.Application.Search;
using Agent.Application.Tools;
using Agent.Domain.Tools;
namespace Agent.Infrastructure.Tools.WebSearch;
public sealed class WebSearchTool(IWebSearchProvider search) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("web_search", "Search the web for current information or recent facts.", ["query"], ToolCategory.WebSearch);
    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        var query = Convert.ToString(context.Arguments.GetValueOrDefault("query") ?? context.Arguments.GetValueOrDefault("input")) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return ToolResult.Failure(0, "", Definition.Name, context.Arguments, "Search query is required.");
        var output = await search.SearchAsync(query, cancellationToken);
        return ToolResult.Success(0, "", Definition.Name, context.Arguments, output);
    }
}
