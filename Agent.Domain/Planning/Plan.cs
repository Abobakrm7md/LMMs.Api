namespace Agent.Domain.Planning;

public enum PlanKind { DirectAnswer, SingleTool, MultiStep }
public enum ExecutionAction { Continue, ExecuteStep, Complete, Abort }

public sealed class PlanStep
{
    public int Id { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? Tool { get; init; }
    public IReadOnlyDictionary<string, object?> Input { get; init; } = new Dictionary<string, object?>();
}

public sealed class Plan
{
    public string Goal { get; init; } = string.Empty;
    public PlanKind Kind { get; init; }
    public IReadOnlyList<PlanStep> Steps { get; init; } = [];
    public static Plan Direct(string goal) => new() { Goal = goal, Kind = PlanKind.DirectAnswer };
}

public sealed record ExecutionDirective(ExecutionAction Action, PlanStep? Step, string Reason)
{
    public static ExecutionDirective Continue(string reason) => new(ExecutionAction.Continue, null, reason);
    public static ExecutionDirective Complete(string reason) => new(ExecutionAction.Complete, null, reason);
    public static ExecutionDirective Abort(string reason) => new(ExecutionAction.Abort, null, reason);
    public static ExecutionDirective Run(PlanStep step, string reason) => new(ExecutionAction.ExecuteStep, step, reason);
}
