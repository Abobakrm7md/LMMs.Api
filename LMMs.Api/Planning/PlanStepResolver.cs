using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LMMs.Api.Planning
{
    public static class PlanStepResolver
    {
        private static readonly Regex PlaceholderPattern = new(
            @"\{\{\s*(?:step[:\.]?)?(?<id>\d+)(?:\.result)?\s*\}\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BareStepPattern = new(
            @"\bstep(?<id>\d+)(?:\.result)?\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IsoDatePattern = new(
            @"\b(?<date>\d{4}-\d{2}-\d{2})(?:[T\s]\d{2}:\d{2}(?::\d{2})?)?\b",
            RegexOptions.Compiled);

        private static readonly Regex MathExpressionPattern = new(
            @"^[\d\s+\-*/().,%]+$",
            RegexOptions.Compiled);

        public static PlanStep Resolve(PlanStep step, IReadOnlyList<ToolExecutionResult> prior)
        {
            var resolved = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in step.Input)
                resolved[pair.Key] = pair.Value is string s ? Substitute(s, prior, numericContext: false) : pair.Value;

            var resolvedStep = new PlanStep
            {
                Id = step.Id,
                Description = Substitute(step.Description, prior, numericContext: false),
                Tool = step.Tool,
                Input = resolved
            };

            return IsCalculator(resolvedStep)
                ? BindCalculatorInput(resolvedStep, prior)
                : resolvedStep;
        }

        public static bool HasUnresolvedPlaceholders(PlanStep step) =>
            step.Input.Values.OfType<string>().Any(HasPlaceholder);

        public static bool NeedsCalculatorRewrite(PlanStep step)
        {
            if (!IsCalculator(step))
                return false;

            return !TryGetCalculatorExpression(step, out var expression)
                   || !IsValidMathExpression(expression);
        }

        public static bool HasPlaceholder(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return PlaceholderPattern.IsMatch(value) || BareStepPattern.IsMatch(value);
        }

        public static string Substitute(string template, IReadOnlyList<ToolExecutionResult> prior, bool numericContext)
        {
            if (string.IsNullOrEmpty(template) || prior.Count == 0)
                return template;

            var result = PlaceholderPattern.Replace(template, match => ReplaceStep(match, prior, numericContext, match.Value));
            result = BareStepPattern.Replace(result, match => ReplaceStep(match, prior, numericContext, match.Value));
            return result;
        }

        private static PlanStep BindCalculatorInput(PlanStep step, IReadOnlyList<ToolExecutionResult> prior)
        {
            if (TryGetCalculatorExpression(step, out var expression))
            {
                if (TryParseObjectMap(expression, out var mapped))
                    return BindCalculatorInput(new PlanStep
                    {
                        Id = step.Id,
                        Description = step.Description,
                        Tool = step.Tool,
                        Input = mapped
                    }, prior);

                expression = Substitute(expression, prior, numericContext: true);
                expression = ReplaceDatesWithDayOfMonth(expression);
                return WithCalculatorExpression(step, expression);
            }

            var numbers = new List<string>();
            foreach (var value in step.Input.Values)
            {
                var text = value?.ToString();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                text = Substitute(text, prior, numericContext: true);
                if (TryCoerceNumber(text, out var number))
                    numbers.Add(number);
            }

            if (numbers.Count == 0)
                return step;

            var op = OperatorFrom(step.Description);
            return WithCalculatorExpression(step, string.Join($" {op} ", numbers));
        }

        private static PlanStep WithCalculatorExpression(PlanStep step, string expression) =>
            new()
            {
                Id = step.Id,
                Description = step.Description,
                Tool = step.Tool,
                Input = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["input"] = expression
                }
            };

        private static bool TryParseObjectMap(string? text, out IReadOnlyDictionary<string, object?> map)
        {
            map = new Dictionary<string, object?>();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var json = text.Trim();
            if (!json.StartsWith('{') || !json.EndsWith('}'))
                return false;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return false;

                var parsed = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in document.RootElement.EnumerateObject())
                    parsed[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();

                if (parsed.Count == 0)
                    return false;

                map = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool TryGetCalculatorExpression(PlanStep step, out string expression)
        {
            foreach (var key in new[] { "input", "expression", "query" })
            {
                if (step.Input.TryGetValue(key, out var value) && value is not null)
                {
                    expression = value.ToString() ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(expression);
                }
            }

            if (step.Input.Count == 1)
            {
                expression = step.Input.Values.First()?.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(expression) && !HasPlaceholder(expression);
            }

            expression = string.Empty;
            return false;
        }

        private static string ReplaceStep(
            Match match,
            IReadOnlyList<ToolExecutionResult> prior,
            bool numericContext,
            string fallback)
        {
            if (!int.TryParse(match.Groups["id"].Value, out var id))
                return fallback;

            var found = prior.FirstOrDefault(r => r.StepId == id);
            if (found is null)
                return fallback;

            if (numericContext && TryCoerceNumber(found.Output, out var number))
                return number;

            return found.Output;
        }

        private static string ReplaceDatesWithDayOfMonth(string expression) =>
            IsoDatePattern.Replace(expression, match =>
                DateTime.TryParse(match.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date.Day.ToString(CultureInfo.InvariantCulture)
                    : match.Value);

        private static bool TryCoerceNumber(string? text, out string number)
        {
            number = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var trimmed = text.Trim();
            if (double.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                number = value.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
            {
                number = date.Day.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            var iso = IsoDatePattern.Match(trimmed);
            if (iso.Success &&
                DateTime.TryParse(iso.Groups["date"].Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                number = date.Day.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        private static char OperatorFrom(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return '+';

            if (description.Contains("multipl", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("product", StringComparison.OrdinalIgnoreCase))
                return '*';

            if (description.Contains("subtract", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("minus", StringComparison.OrdinalIgnoreCase))
                return '-';

            if (description.Contains("divid", StringComparison.OrdinalIgnoreCase))
                return '/';

            return '+';
        }

        public static bool IsValidMathExpression(string? expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            var normalized = expression.Replace('×', '*').Replace('÷', '/').Replace('x', '*').Replace('X', '*');
            return MathExpressionPattern.IsMatch(normalized) && normalized.Any(char.IsDigit);
        }

        private static bool IsCalculator(PlanStep step) =>
            !string.IsNullOrWhiteSpace(step.Tool) &&
            step.Tool.Contains("calc", StringComparison.OrdinalIgnoreCase);
    }
}
