using Agent.Application.Chat;
using System.Text.Json;

namespace Agent.Application.Agents;

public sealed record PullRequestFile(string Path, string Patch, int? OldLine = null, int? NewLine = null);
public sealed record PullRequestData(string Provider, string Repository, string Number, string Title, IReadOnlyList<PullRequestFile> Files);

public interface IPullRequestProvider
{
    string Name { get; }
    bool CanHandle(Uri pullRequestUrl);
    Task<PullRequestData> GetAsync(Uri pullRequestUrl, CancellationToken cancellationToken);
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
public sealed record ReviewFinding(ReviewSeverity Severity, ReviewCategory Category, string Title, string Description, string? File, int? Line, string WhyItMatters, string SuggestedFix);
public sealed record CodeReviewResult(string Provider, string Repository, string PullRequest, IReadOnlyList<ReviewFinding> Findings);

public sealed class CodeReviewAgent(IPullRequestProviderRegistry providers, IChatModel chatModel) : ISpecializedAgent
{
    public AgentDescriptor Descriptor { get; } = new("CodeReviewAgent", "Reviews pull request changes and returns actionable, evidence-based findings.", ["code-review", "pull-request", "security", "quality"]);
    public bool CanHandle(AgentTurnRequest request) => TryGetUrl(request.Prompt, out _);

    public async IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(AgentTurnRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!TryGetUrl(request.Prompt, out var url)) throw new InvalidOperationException("A supported pull request URL is required for code review.");
        var provider = providers.Find(url) ?? throw new InvalidOperationException("The pull request provider is not supported.");
        var pullRequest = await provider.GetAsync(url, cancellationToken);
        if (pullRequest.Files.Count == 0) { yield return new TextDeltaProduced("The pull request has no reviewable changes."); yield break; }
        var prompt = $"Review this pull request. The user's objective is: {request.Prompt}\nReturn ONLY JSON array of findings. Do not invent issues; report concrete defects, risks, and actionable improvements. Distinguish facts from style preferences. Each item must have severity (Critical, High, Medium, Low, Info), category (Bug, Security, Performance, Architecture, Maintainability, CodeQuality, Testing), title, description, file, line, whyItMatters, suggestedFix.\nChanges:\n{string.Join("\n\n", pullRequest.Files.Select(f => $"FILE: {f.Path}\n{f.Patch}"))}";
        var response = await chatModel.CompleteAsync([new Agent.Domain.Conversations.Message(Agent.Domain.Conversations.MessageRole.System, "You are a senior software engineer performing a practical code review."), new Agent.Domain.Conversations.Message(Agent.Domain.Conversations.MessageRole.User, prompt)], new ChatModelOptions(0), cancellationToken);
        var findings = Parse(response);
        var text = findings.Count == 0 ? "No actionable findings were identified." : string.Join("\n", findings.Select(f => $"[{f.Severity}] {f.File}:{f.Line} {f.Title} — {f.SuggestedFix}"));
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
        var normalized = json.Trim();
        if (normalized.StartsWith("```") )
        {
            var firstLine = normalized.IndexOf('\n');
            var lastFence = normalized.LastIndexOf("```");
            if (firstLine >= 0 && lastFence > firstLine)
                normalized = normalized[(firstLine + 1)..lastFence].Trim();
        }

        try
        {
            // Models occasionally prepend an explanation despite the JSON-only instruction.
            // Extract a JSON object/array when one is present; otherwise return a controlled empty result.
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

            using var document = System.Text.Json.JsonDocument.Parse(normalized);
            var element = document.RootElement;
            if (element.ValueKind == System.Text.Json.JsonValueKind.Object && element.TryGetProperty("findings", out var wrapped))
                element = wrapped;
            if (element.ValueKind != System.Text.Json.JsonValueKind.Array) return [];
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
            return element.Deserialize<List<ReviewFinding>>(options) ?? [];
        }
        catch (System.Text.Json.JsonException) { return []; }
    }
}
