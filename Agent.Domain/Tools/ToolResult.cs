using Agent.Domain.Planning;

namespace Agent.Domain.Tools;

public sealed class ToolResult
{
    public int StepId { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? ToolName { get; init; }
    public IReadOnlyDictionary<string, object?> Arguments { get; init; } = new Dictionary<string, object?>();
    public string Output { get; init; } = string.Empty;
    public bool Succeeded { get; init; }
    public bool IsEmpty { get; init; }
    public string? Error { get; init; }

    public static ToolResult Success(int id, string description, string? tool, IReadOnlyDictionary<string, object?> args, string output)
    {
        var empty = string.IsNullOrWhiteSpace(output);
        return new ToolResult { StepId = id, Description = description, ToolName = tool, Arguments = args,
            Output = output, Succeeded = !empty, IsEmpty = empty, Error = empty ? "Empty tool result" : null };
    }

    public static ToolResult Failure(int id, string description, string? tool, IReadOnlyDictionary<string, object?> args, string error, string? output = null) =>
        new() { StepId = id, Description = description, ToolName = tool, Arguments = args, Output = output ?? string.Empty,
            Succeeded = false, IsEmpty = string.IsNullOrWhiteSpace(output), Error = error };
}

public sealed class ExecutionResult
{
    public Planning.Plan Plan { get; init; } = Planning.Plan.Direct(string.Empty);
    public IReadOnlyList<ToolResult> StepResults { get; init; } = [];
    public bool Completed { get; init; }
    public bool Aborted { get; init; }
    public string? StatusDetail { get; init; }
}
