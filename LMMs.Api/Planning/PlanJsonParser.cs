using System.Text.Json;

namespace LMMs.Api.Planning
{
    public static class PlanJsonParser
    {
        public static AgentPlan ParsePlan(string? raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrWhiteSpace(json))
                return AgentPlan.Direct("Answer the user directly");

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("action", out var action) &&
                string.Equals(action.GetString(), "answer", StringComparison.OrdinalIgnoreCase))
            {
                return AgentPlan.Direct("Answer the user directly");
            }

            var goal = ReadString(root, "goal") ?? "Complete the user request";
            var steps = ReadSteps(root);
            var kind = ReadKind(root, steps.Count);

            if (kind == PlanKind.DirectAnswer || steps.Count == 0)
                return AgentPlan.Direct(goal);

            return new AgentPlan
            {
                Goal = goal,
                Kind = kind,
                Steps = steps
            };
        }

        public static ExecutionDirective ParseDirective(string? raw, int fallbackStepId)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrWhiteSpace(json))
                return ExecutionDirective.Abort("Could not parse next-action JSON");

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var actionText = ReadString(root, "action") ?? "abort";
            var reason = ReadString(root, "reason") ?? actionText;

            if (string.Equals(actionText, "complete", StringComparison.OrdinalIgnoreCase))
                return ExecutionDirective.Complete(reason);

            if (string.Equals(actionText, "continue", StringComparison.OrdinalIgnoreCase))
                return ExecutionDirective.Continue(reason);

            if (string.Equals(actionText, "abort", StringComparison.OrdinalIgnoreCase))
                return ExecutionDirective.Abort(reason);

            if (string.Equals(actionText, "execute_step", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionText, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var step = ReadSingleStep(root, fallbackStepId) ??
                           (root.TryGetProperty("step", out var stepEl) ? ReadStep(stepEl, fallbackStepId) : null);

                if (step is null || string.IsNullOrWhiteSpace(step.Tool))
                    return ExecutionDirective.Abort("Next step was missing a tool name");

                return ExecutionDirective.Run(step, reason);
            }

            return ExecutionDirective.Abort($"Unknown next action '{actionText}'");
        }

        public static string ExtractJson(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var text = raw.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = text.IndexOf('\n');
                if (firstNewline >= 0)
                    text = text[(firstNewline + 1)..];
                var fence = text.LastIndexOf("```", StringComparison.Ordinal);
                if (fence >= 0)
                    text = text[..fence];
                text = text.Trim();
            }

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
                return string.Empty;

            return text[start..(end + 1)];
        }

        private static PlanKind ReadKind(JsonElement root, int stepCount)
        {
            var kindText = ReadString(root, "kind") ?? ReadString(root, "type");
            if (!string.IsNullOrWhiteSpace(kindText))
            {
                if (kindText.Contains("direct", StringComparison.OrdinalIgnoreCase) ||
                    kindText.Contains("none", StringComparison.OrdinalIgnoreCase))
                    return PlanKind.DirectAnswer;
                if (kindText.Contains("multi", StringComparison.OrdinalIgnoreCase))
                    return PlanKind.MultiStep;
                if (kindText.Contains("single", StringComparison.OrdinalIgnoreCase) ||
                    kindText.Contains("tool", StringComparison.OrdinalIgnoreCase))
                    return PlanKind.SingleTool;
            }

            return stepCount switch
            {
                0 => PlanKind.DirectAnswer,
                1 => PlanKind.SingleTool,
                _ => PlanKind.MultiStep
            };
        }

        private static List<PlanStep> ReadSteps(JsonElement root)
        {
            if (!root.TryGetProperty("steps", out var stepsEl) || stepsEl.ValueKind != JsonValueKind.Array)
                return [];

            var steps = new List<PlanStep>();
            var index = 1;
            foreach (var item in stepsEl.EnumerateArray())
            {
                var step = ReadStep(item, index);
                if (step is not null)
                    steps.Add(step);
                index++;
            }

            return steps;
        }

        private static PlanStep? ReadSingleStep(JsonElement root, int fallbackId)
        {
            var tool = ReadString(root, "tool");
            if (string.IsNullOrWhiteSpace(tool) && root.TryGetProperty("step", out var nested))
                return ReadStep(nested, fallbackId);

            if (string.IsNullOrWhiteSpace(tool))
                return null;

            return new PlanStep
            {
                Id = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id) ? id : fallbackId,
                Description = ReadString(root, "description") ?? $"Call {tool}",
                Tool = tool,
                Input = ReadInput(root)
            };
        }

        private static PlanStep? ReadStep(JsonElement item, int fallbackId)
        {
            var tool = ReadString(item, "tool");
            if (string.IsNullOrWhiteSpace(tool))
                return null;

            var id = fallbackId;
            if (item.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var parsed))
                id = parsed;

            return new PlanStep
            {
                Id = id,
                Description = ReadString(item, "description") ?? $"Call {tool}",
                Tool = tool,
                Input = ReadInput(item)
            };
        }

        private static IReadOnlyDictionary<string, object?> ReadInput(JsonElement parent)
        {
            if (!parent.TryGetProperty("input", out var input))
                return new Dictionary<string, object?>();

            if (input.ValueKind == JsonValueKind.String)
                return new Dictionary<string, object?> { ["input"] = input.GetString() };

            if (input.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, object?>();

            var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in input.EnumerateObject())
                map[prop.Name] = ConvertJson(prop.Value);

            return map;
        }

        private static object? ConvertJson(JsonElement value) =>
            value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => value.GetRawText()
            };

        private static string? ReadString(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
