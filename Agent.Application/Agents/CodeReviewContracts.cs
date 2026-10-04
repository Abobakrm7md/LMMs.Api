using Agent.Application.Chat;
using System.Text.Json;
using System.Text.Json.Serialization;

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
public sealed record CodeReviewResult(string Provider, string Repository, string PullRequest, IReadOnlyList<ReviewFinding> Findings, string? RawReview);

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
        string response = string.Empty;
        try
        {
            response = await chatModel.CompleteAsync([new Agent.Domain.Conversations.Message(Agent.Domain.Conversations.MessageRole.System
            , "You are a senior software engineer performing a practical code review."),
            new Agent.Domain.Conversations.Message(Agent.Domain.Conversations.MessageRole.User, prompt)], new ChatModelOptions(0), cancellationToken);
        }catch(Exception ex)
        {
            Console.WriteLine(ex);
        }
        var review = Parse(response);

        if (review.Findings.Count > 0)
        {
            var formatted = string.Join(
                "\n",
                review.Findings.Select(f =>
                    $"[{f.Severity}] {f.File}:{f.Line} {f.Title} — {f.SuggestedFix}"));

            yield return new TextDeltaProduced(formatted);
        }
        else if (!string.IsNullOrWhiteSpace(review.RawReview))
        {
            yield return new TextDeltaProduced(review.RawReview);
        }
        else
        {
            yield return new TextDeltaProduced(
                "The model returned an empty code review.");
        }

       // yield return new TextDeltaProduced(text);
    }

    private static bool TryGetUrl(string prompt, out Uri url)
    {
        url = null!;
        var candidate = prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => Uri.TryCreate(x.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp));
        return candidate is not null && Uri.TryCreate(candidate, UriKind.Absolute, out url);
    }
    internal static CodeReviewResult Parse(string? response)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var normalized = json.Trim('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');
        if (normalized.StartsWith("```") )
        {
            return new CodeReviewResult(" ", "", " ", [], normalized);
        }

        try
        {
            using var document = JsonDocument.Parse(normalized);
            var element = document.RootElement;

            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty("findings", out var findingsElement))
            {
                element = findingsElement;
            }

            if (element.ValueKind != JsonValueKind.Array)
                return new CodeReviewResult(" ", "", " ", [], normalized);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            options.Converters.Add(new JsonStringEnumConverter());

            var findings =
                element.Deserialize<List<ReviewFinding>>(options) ?? [];

            return new CodeReviewResult(" ", "", " ", findings, null);
        }
        catch (JsonException)
        {
            return new CodeReviewResult(" ", "", " ", [], normalized);
        }
    }
}
