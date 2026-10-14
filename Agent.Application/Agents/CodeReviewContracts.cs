using Agent.Application.Chat;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agent.Application.Agents;

public sealed record PullRequestFile(string Path, string Patch, int? OldLine = null, int? NewLine = null);
public sealed record PullRequestData(string Provider, string Repository, string Number, string Title, string HeadSha, IReadOnlyList<PullRequestFile> Files);

public interface IPullRequestProvider
{
    string Name { get; }
    bool CanHandle(Uri pullRequestUrl);
    Task<PullRequestData> GetAsync(Uri pullRequestUrl, CancellationToken cancellationToken);
    Task AddFindingCommentsAsync(Uri pullRequestUrl, IReadOnlyList<ReviewFinding> findings, CancellationToken cancellationToken);
}

public interface IPullRequestProviderRegistry
{
    IPullRequestProvider? Find(Uri url);
}

public sealed class PullRequestProviderRegistry(IEnumerable<IPullRequestProvider> providers) : IPullRequestProviderRegistry
{
    private readonly IReadOnlyList<IPullRequestProvider> providers = providers.ToList();
    public IPullRequestProvider? Find(Uri url) => providers.FirstOrDefault(p => p.CanHandle(url));
}

public enum ReviewSeverity { Critical, High, Medium, Low, Info }
public enum ReviewCategory { Bug, Security, Performance, Architecture, Maintainability, CodeQuality, Testing }

/// <summary>
/// A code review finding produced by the LLM.
/// <para>
/// Each property carries a <see cref="JsonPropertyNameAttribute"/> so that
/// deserialization succeeds regardless of whether the LLM emits lowercase,
/// PascalCase, or camelCase keys.  The <c>File</c> property also accepts
/// common synonyms (<c>filename</c>, <c>filePath</c>, <c>path</c>) via a
/// manual fallback in <see cref="CodeReviewAgent.Parse"/>.
/// </para>
/// </summary>
public sealed record ReviewFinding(
    [property: JsonPropertyName("severity")]      ReviewSeverity Severity,
    [property: JsonPropertyName("category")]       ReviewCategory Category,
    [property: JsonPropertyName("title")]          string? Title,
    [property: JsonPropertyName("description")]    string? Description,
    [property: JsonPropertyName("file")]           string? File,
    [property: JsonPropertyName("line")]           int?    Line,
    [property: JsonPropertyName("whyItMatters")]   string? WhyItMatters,
    [property: JsonPropertyName("suggestedFix")]   string? SuggestedFix,
    [property: JsonPropertyName("message")]        string? Message = null);
public sealed record CodeReviewResult(string Provider, string Repository, string PullRequest, IReadOnlyList<ReviewFinding> Findings);

public sealed class CodeReviewAgent(IPullRequestProviderRegistry providers, IChatModel chatModel) : ISpecializedAgent
{
    public AgentDescriptor Descriptor { get; } = new("CodeReviewAgent", "Reviews pull request changes and returns actionable, evidence-based findings.", ["code-review", "pull-request", "security", "quality"]);
    public bool CanHandle(AgentTurnRequest request) => TryGetUrl(request.Prompt, out _);

    private const string ReviewSystemPrompt =
        "You are a senior software engineer performing a practical code review.\n" +
        "\n" +
        "RULES — you MUST follow every one:\n" +
        "1. Return ONLY a single JSON array. No markdown fences. No prose before or after.\n" +
        "2. If you find no issues, return an empty array: []\n" +
        "3. Each object MUST have exactly these lowercase keys:\n" +
        "   severity, category, title, description, file, line, whyItMatters, suggestedFix\n" +
        "4. \"severity\" is one of: Critical, High, Medium, Low, Info\n" +
        "5. \"category\" is one of: Bug, Security, Performance, Architecture, Maintainability, CodeQuality, Testing\n" +
        "6. \"file\" is the EXACT path from the FILE: header (e.g. \"src/Foo.cs\")\n" +
        "7. \"line\" is an integer — the line number in the NEW version of the file\n" +
        "\n" +
        "EXAMPLE OUTPUT:\n" +
        "[\n" +
        "  {\n" +
        "    \"severity\": \"High\",\n" +
        "    \"category\": \"Bug\",\n" +
        "    \"title\": \"Null reference on empty input\",\n" +
        "    \"description\": \"The method does not guard against null or empty arrays.\",\n" +
        "    \"file\": \"src/Parser.cs\",\n" +
        "    \"line\": 42,\n" +
        "    \"whyItMatters\": \"Calling code that passes null will crash at runtime.\",\n" +
        "    \"suggestedFix\": \"Add a null/empty check at the start of the method.\"\n" +
        "  }\n" +
        "]\n" +
        "\n" +
        "Do NOT explain. Do NOT add headings. Output the JSON array directly.";

    private const string ReformatSystemPrompt =
        "You are a JSON formatter. The user will give you a code review in prose.\n" +
        "Convert it into a JSON array of finding objects. Each object MUST have exactly these lowercase keys:\n" +
        "  severity, category, title, description, file, line, whyItMatters, suggestedFix\n" +
        "Rules:\n" +
        "- severity is one of: Critical, High, Medium, Low, Info\n" +
        "- category is one of: Bug, Security, Performance, Architecture, Maintainability, CodeQuality, Testing\n" +
        "- file is the file path mentioned in the review (or null if none)\n" +
        "- line is the line number mentioned (or null if none)\n" +
        "- If the review mentions no actionable findings, return: []\n" +
        "Output ONLY the JSON array. No markdown fences. No explanation.";

    public async IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(AgentTurnRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!TryGetUrl(request.Prompt, out var url)) throw new InvalidOperationException("A supported pull request URL is required for code review.");
        var provider = providers.Find(url) ?? throw new InvalidOperationException("The pull request provider is not supported.");
        var pullRequest = await provider.GetAsync(url, cancellationToken);
        if (pullRequest.Files.Count == 0) { yield return new TextDeltaProduced("The pull request has no reviewable changes."); yield break; }
        var prompt = $"Review this pull request. The user's objective is: {request.Prompt}\n" +
            $"Changes:\n{string.Join("\n\n", pullRequest.Files.Select(f => $"FILE: {f.Path}\n{f.Patch}"))}";

        // ── Call 1: Ask the LLM to review the PR ───────────────────────────
        var response = await chatModel.CompleteAsync([
            new Agent.Domain.Conversations.Message(Agent.Domain.Conversations.MessageRole.System, ReviewSystemPrompt),
            new Agent.Domain.Conversations.Message(Agent.Domain.Conversations.MessageRole.User, prompt)
        ], new ChatModelOptions(0), cancellationToken);

        var findings = Parse(response);

        // ── Call 2 (if needed): Ask the LLM to reformat its own prose as JSON
        if (findings.Count == 0 && !string.IsNullOrWhiteSpace(response))
        {
            var reformatted = await chatModel.CompleteAsync([
                new Agent.Domain.Conversations.Message(Agent.Domain.Conversations.MessageRole.System, ReformatSystemPrompt),
                new Agent.Domain.Conversations.Message(Agent.Domain.Conversations.MessageRole.User, response)
            ], new ChatModelOptions(0), cancellationToken);

            findings = Parse(reformatted);
        }

        // ── Post to GitHub ────────────────────────────────────────────────────
        if (findings.Count > 0)
        {
            await provider.AddFindingCommentsAsync(url, findings, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(response))
        {
            // Last resort: post the raw prose as a single general PR comment.
            await provider.AddFindingCommentsAsync(url, [
                new ReviewFinding(
                    Severity: ReviewSeverity.Info,
                    Category: ReviewCategory.CodeQuality,
                    Title: "Code review",
                    Description: response.Trim(),
                    File: null, Line: null,
                    WhyItMatters: null, SuggestedFix: null)
            ], cancellationToken);
        }

        // ── Return text to the chat UI ────────────────────────────────────────
        var text = findings.Count > 0
            ? string.Join("\n", findings.Select(f => $"[{f.Severity}] {f.File}:{f.Line} {f.Title} — {f.SuggestedFix}"))
            : string.IsNullOrWhiteSpace(response)
                ? "The model returned an empty review."
                : response.Trim();
        yield return new TextDeltaProduced(text);
    }

    private static bool TryGetUrl(string prompt, out Uri url)
    {
        url = null!;
        var candidate = prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => Uri.TryCreate(x.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp));
        return candidate is not null && Uri.TryCreate(candidate, UriKind.Absolute, out url);
    }
    internal static IReadOnlyList<ReviewFinding> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var normalized = json.Trim('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');
        if (normalized.StartsWith("```") )
        {
            var firstLine = normalized.IndexOf('\n');
            var lastFence = normalized.LastIndexOf("```");
            if (firstLine >= 0 && lastFence > firstLine)
                normalized = normalized[(firstLine + 1)..lastFence].Trim();
        }

        try
        {
            // Models may prepend an explanation, headings, or recommendations before the JSON.
            // Extract the JSON payload before deciding that the response is unstructured.
            var arrayStart = normalized.IndexOf('[');
            var arrayEnd = normalized.LastIndexOf(']');
            if (arrayStart >= 0 && arrayEnd > arrayStart)
                normalized = normalized[arrayStart..(arrayEnd + 1)];
            else
            {
                var objectStart = normalized.IndexOf('{');
                var objectEnd = normalized.LastIndexOf('}');
                if (objectStart >= 0 && objectEnd > objectStart)
                    normalized = normalized[objectStart..(objectEnd + 1)];
            }

            if (normalized.Length == 0 || (normalized[0] != '[' && normalized[0] != '{'))
                return [];

            using var document = System.Text.Json.JsonDocument.Parse(normalized);
            var element = document.RootElement;
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object && element.TryGetProperty("findings", out var wrapped))
                element = wrapped;
            if (element.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

            var findings = element.Deserialize<List<ReviewFinding>>(options) ?? [];

            // ── Resilient post-processing ────────────────────────────────────
            // LLMs frequently return the file path under unexpected key names
            // ("filename", "filePath", "path", "fileName", "file_path", …) or
            // omit it entirely.  Walk the raw JSON array a second time and fill
            // in any File / Line values that the standard deserializer missed.
            for (var i = 0; i < findings.Count; i++)
            {
                if (i >= element.GetArrayLength()) break;
                var raw = element[i];

                var f = findings[i];

                // ── Patch File if null ──────────────────────────────────────
                if (string.IsNullOrWhiteSpace(f.File))
                {
                    var resolvedFile = TryGetStringFromAnyKey(raw,
                        "file", "filename", "fileName", "filePath", "filepath",
                        "path", "file_path", "source", "sourceFile", "src");

                    if (!string.IsNullOrWhiteSpace(resolvedFile))
                        f = f with { File = resolvedFile };
                }

                // ── Patch Line if null ─────────────────────────────────────
                if (f.Line is null)
                {
                    var resolvedLine = TryGetIntFromAnyKey(raw,
                        "line", "lineNumber", "line_number", "lineno",
                        "startLine", "start_line", "newLine", "new_line");

                    if (resolvedLine is > 0)
                        f = f with { Line = resolvedLine };
                }

                findings[i] = f;
            }

            return findings;
        }
        catch (System.Text.Json.JsonException) { return []; }
        catch (InvalidOperationException) { return []; }
    }

    // ── Helpers for resilient JSON key lookup ─────────────────────────────────

    private static string? TryGetStringFromAnyKey(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
            {
                var s = val.GetString();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        // Case-insensitive sweep as last resort
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (keys.Contains(prop.Name, StringComparer.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var s = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        return null;
    }

    private static int? TryGetIntFromAnyKey(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out var n))
                return n;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (keys.Contains(prop.Name, StringComparer.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Number
                    && prop.Value.TryGetInt32(out var n))
                    return n;
            }
        }
        return null;
    }
}
