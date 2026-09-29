namespace Agent.Application.Tools;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyList<IAgentTool> _tools;
    public ToolRegistry(IEnumerable<IAgentTool> tools) => _tools = tools.ToList();

    public IReadOnlyList<IAgentTool> Select(bool enableTools, bool enableWebSearch, bool hasAttachment)
    {
        if (!enableTools && !hasAttachment) return [];
        IEnumerable<IAgentTool> selected = _tools;
        if (!enableTools) selected = selected.Where(t => t.Definition.Category == ToolCategory.File);
        else
        {
            if (!enableWebSearch) selected = selected.Where(t => t.Definition.Category != ToolCategory.WebSearch);
            if (!hasAttachment) selected = selected.Where(t => t.Definition.Category != ToolCategory.File);
        }
        return selected.ToList();
    }

    public IAgentTool? Find(string name, IReadOnlyList<IAgentTool> availableTools)
    {
        var normalized = Normalize(name);
        return availableTools.FirstOrDefault(t => Normalize(t.Definition.Name) == normalized);
    }

    private static string Normalize(string name) => name.Trim().ToLowerInvariant() switch
    {
        "calculator" or "calc" or "math" => "calculator",
        "gettime" or "time" or "clock" => "gettime",
        "getdate" or "date" or "day" or "datetime" => "getdate",
        "readfile" or "file" => "read_file",
        "websearch" or "search" => "web_search",
        var value => value.Replace(" ", string.Empty)
    };
}
