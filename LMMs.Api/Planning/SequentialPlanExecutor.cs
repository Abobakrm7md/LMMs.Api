using LMMs.Api.Interfaces;

namespace LMMs.Api.Planning
{
    public sealed class SequentialPlanExecutor : IPlanExecutor
    {
        private const int MaxSteps = 8;
        private const int MaxAdaptiveSteps = 3;

        private readonly IToolInvoker _toolInvoker;
        private readonly IPlanner _planner;
        private readonly ILogger<SequentialPlanExecutor> _logger;

        public SequentialPlanExecutor(
            IToolInvoker toolInvoker,
            IPlanner planner,
            ILogger<SequentialPlanExecutor> logger)
        {
            _toolInvoker = toolInvoker;
            _planner = planner;
            _logger = logger;
        }

        public async Task<PlanExecutionResult> ExecuteAsync(
            AgentPlan plan,
            IReadOnlyList<IAgentTool> tools,
            string userGoal,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (plan.Kind == PlanKind.DirectAnswer || plan.Steps.Count == 0)
            {
                _logger.LogInformation("Executor skipped; plan has no tool steps");
                return new PlanExecutionResult
                {
                    Plan = plan,
                    Completed = true,
                    StepResults = []
                };
            }

            _logger.LogInformation("Executing plan kind={Kind} with {StepCount} step(s)", plan.Kind, plan.Steps.Count);

            var descriptors = AgentToolSelector.Describe(tools);
            var remaining = new Queue<PlanStep>(plan.Steps);
            var results = new List<ToolExecutionResult>();
            var adaptiveUsed = 0;
            var completed = false;
            var aborted = false;
            string? status = null;

            while ((remaining.Count > 0 || adaptiveUsed > 0) && results.Count < MaxSteps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (remaining.Count == 0)
                    break;

                var step = PlanStepResolver.Resolve(remaining.Dequeue(), results);
                _logger.LogInformation("Starting step {StepId} tool={Tool}", step.Id, step.Tool);

                if (string.IsNullOrWhiteSpace(step.Tool)
                    || PlanStepResolver.HasUnresolvedPlaceholders(step)
                    || PlanStepResolver.NeedsCalculatorRewrite(step))
                {
                    var reason = PlanStepResolver.NeedsCalculatorRewrite(step)
                        ? "Calculator requires a single math expression using prior numeric results. If a prior result is a date, use the day of the month."
                        : "Step input is incomplete or invalid";
                    var advised = await AdviseAsync(
                        plan, userGoal, results, step, reason, descriptors, cancellationToken);
                    if (!TryApplyDirective(advised, remaining, results, ref adaptiveUsed, ref completed, ref aborted, ref status))
                        break;
                    continue;
                }

                var result = await _toolInvoker.InvokeAsync(step, tools, cancellationToken);
                results.Add(result);

                if (result.Succeeded)
                {
                    _logger.LogInformation("Step {StepId} succeeded", step.Id);
                    continue;
                }

                _logger.LogWarning("Step {StepId} failed: {Error}", step.Id, result.Error);
                var directive = await AdviseAsync(
                    plan, userGoal, results, step, result.Error ?? "Tool failed", descriptors, cancellationToken);
                if (!TryApplyDirective(directive, remaining, results, ref adaptiveUsed, ref completed, ref aborted, ref status))
                    break;
            }

            if (!aborted && !completed)
                completed = results.Count > 0 && results.TrueForAll(r => r.Succeeded) && remaining.Count == 0;

            _logger.LogInformation(
                "Plan execution finished completed={Completed} aborted={Aborted} steps={Count}",
                completed, aborted, results.Count);

            return new PlanExecutionResult
            {
                Plan = plan,
                StepResults = results,
                Completed = completed && !aborted,
                Aborted = aborted,
                StatusDetail = status
            };
        }

        private async Task<ExecutionDirective> AdviseAsync(
            AgentPlan plan,
            string userGoal,
            List<ToolExecutionResult> results,
            PlanStep? current,
            string reason,
            IReadOnlyList<ToolDescriptor> tools,
            CancellationToken cancellationToken)
        {
            return await _planner.DecideNextAsync(new ExecutionState
            {
                UserGoal = userGoal,
                Plan = plan,
                ResultsSoFar = results,
                CurrentStep = current,
                Reason = reason,
                Tools = tools
            }, cancellationToken);
        }

        private bool TryApplyDirective(
            ExecutionDirective directive,
            Queue<PlanStep> remaining,
            List<ToolExecutionResult> results,
            ref int adaptiveUsed,
            ref bool completed,
            ref bool aborted,
            ref string? status)
        {
            status = directive.Reason;
            _logger.LogInformation("Applying execution directive {Action}", directive.Action);

            switch (directive.Action)
            {
                case ExecutionAction.Complete:
                    remaining.Clear();
                    completed = true;
                    return false;
                case ExecutionAction.Abort:
                    remaining.Clear();
                    aborted = true;
                    return false;
                case ExecutionAction.Continue:
                    return remaining.Count > 0;
                case ExecutionAction.ExecuteStep when directive.Step is not null:
                    if (adaptiveUsed >= MaxAdaptiveSteps)
                    {
                        aborted = true;
                        status = "Adaptive step limit reached";
                        _logger.LogWarning("Adaptive step limit reached");
                        return false;
                    }

                    adaptiveUsed++;
                    var next = PlanStepResolver.Resolve(CloneStep(directive.Step, results), results);

                    var replay = new Queue<PlanStep>(remaining);
                    remaining.Clear();
                    remaining.Enqueue(next);
                    while (replay.Count > 0)
                        remaining.Enqueue(replay.Dequeue());
                    return true;
                default:
                    aborted = true;
                    return false;
            }
        }

        private static PlanStep CloneStep(PlanStep step, List<ToolExecutionResult> results)
        {
            var id = step.Id;
            if (id <= 0 || results.Any(r => r.StepId == id))
                id = (results.Count == 0 ? 0 : results.Max(r => r.StepId)) + 1;

            return new PlanStep
            {
                Id = id,
                Description = step.Description,
                Tool = step.Tool,
                Input = step.Input
            };
        }
    }
}
