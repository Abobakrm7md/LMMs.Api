namespace LMMs.Api.Planning
{
    public enum PlanKind
    {
        DirectAnswer = 0,
        SingleTool = 1,
        MultiStep = 2
    }

    public enum ExecutionAction
    {
        Continue = 0,
        ExecuteStep = 1,
        Complete = 2,
        Abort = 3
    }

    public sealed class AgentPlan
    {
        public string Goal { get; init; } = string.Empty;
        public PlanKind Kind { get; init; }
        public IReadOnlyList<PlanStep> Steps { get; init; } = [];

        public static AgentPlan Direct(string goal) => new()
        {
            Goal = goal,
            Kind = PlanKind.DirectAnswer,
            Steps = []
        };
    }

    public sealed class PlanStep
    {
        public int Id { get; init; }
        public string Description { get; init; } = string.Empty;
        public string? Tool { get; init; }
        public IReadOnlyDictionary<string, object?> Input { get; init; } =
            new Dictionary<string, object?>();
    }

    public sealed class ToolExecutionResult
    {
        public int StepId { get; init; }
        public string Description { get; init; } = string.Empty;
        public string? ToolName { get; init; }
        public IReadOnlyDictionary<string, object?> Arguments { get; init; } =
            new Dictionary<string, object?>();
        public string Output { get; init; } = string.Empty;
        public bool Succeeded { get; init; }
        public bool IsEmpty { get; init; }
        public string? Error { get; init; }

        public static ToolExecutionResult Success(
            int stepId,
            string description,
            string? toolName,
            IReadOnlyDictionary<string, object?> arguments,
            string output)
        {
            var empty = string.IsNullOrWhiteSpace(output);
            return new ToolExecutionResult
            {
                StepId = stepId,
                Description = description,
                ToolName = toolName,
                Arguments = arguments,
                Output = output,
                Succeeded = !empty,
                IsEmpty = empty,
                Error = empty ? "Empty tool result" : null
            };
        }

        public static ToolExecutionResult Failure(
            int stepId,
            string description,
            string? toolName,
            IReadOnlyDictionary<string, object?> arguments,
            string error,
            string? output = null) =>
            new()
            {
                StepId = stepId,
                Description = description,
                ToolName = toolName,
                Arguments = arguments,
                Output = output ?? string.Empty,
                Succeeded = false,
                IsEmpty = string.IsNullOrWhiteSpace(output),
                Error = error
            };
    }

    public sealed class PlanExecutionResult
    {
        public AgentPlan Plan { get; init; } = AgentPlan.Direct(string.Empty);
        public IReadOnlyList<ToolExecutionResult> StepResults { get; init; } = [];
        public bool Completed { get; init; }
        public bool Aborted { get; init; }
        public string? StatusDetail { get; init; }
    }

    public sealed class PlanningRequest
    {
        public string UserPrompt { get; init; } = string.Empty;
        public IReadOnlyList<string> RecentConversation { get; init; } = [];
        public IReadOnlyList<ToolDescriptor> Tools { get; init; } = [];
    }

    public sealed class ToolDescriptor
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public IReadOnlyList<string> Parameters { get; init; } = [];
    }

    public sealed class ExecutionState
    {
        public string UserGoal { get; init; } = string.Empty;
        public AgentPlan Plan { get; init; } = AgentPlan.Direct(string.Empty);
        public IReadOnlyList<ToolExecutionResult> ResultsSoFar { get; init; } = [];
        public PlanStep? CurrentStep { get; init; }
        public string Reason { get; init; } = string.Empty;
        public IReadOnlyList<ToolDescriptor> Tools { get; init; } = [];
    }

    public sealed class ExecutionDirective
    {
        public ExecutionAction Action { get; init; }
        public PlanStep? Step { get; init; }
        public string Reason { get; init; } = string.Empty;

        public static ExecutionDirective Continue(string reason) =>
            new() { Action = ExecutionAction.Continue, Reason = reason };

        public static ExecutionDirective Complete(string reason) =>
            new() { Action = ExecutionAction.Complete, Reason = reason };

        public static ExecutionDirective Abort(string reason) =>
            new() { Action = ExecutionAction.Abort, Reason = reason };

        public static ExecutionDirective Run(PlanStep step, string reason) =>
            new() { Action = ExecutionAction.ExecuteStep, Step = step, Reason = reason };
    }
}
