using System.Net.Http.Headers;
using System.Text.Json;
using Agent.Application.Agents;

namespace Agent.Infrastructure.CodeReview;

public sealed class GitHubPullRequestProvider(HttpClient httpClient, IConfiguration configuration) : IPullRequestProvider
{
    public string Name => "GitHub";
    public bool CanHandle(Uri url) => url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 4;
    public async Task<PullRequestData> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var repository = $"{parts[0]}/{parts[1]}"; var number = parts[3];
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/pulls/{number}/files");
        request.Headers.UserAgent.ParseAdd("LMMs.Api"); request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        var token = configuration["GitHub:Token"]; if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The pull request could not be retrieved from the provider.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var files = json.RootElement.EnumerateArray().Select(x => new PullRequestFile(x.GetProperty("filename").GetString() ?? "", x.TryGetProperty("patch", out var patch) ? patch.GetString() ?? "" : "")).ToList();
        return new PullRequestData(Name, repository, number, "", files);
    }

    public async Task AddFindingCommentsAsync(Uri url, IReadOnlyList<ReviewFinding> findings, CancellationToken cancellationToken)
    {
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var repository = $"{parts[0]}/{parts[1]}";
        var number = parts[3];
        var token = configuration["GitHub:Token"];
        foreach (var finding in findings)
        {
            var title = finding.Title ?? finding.Title ?? "Code review finding";
            var details = finding.Description ?? finding.Title ?? "No additional details provided.";
            var body = $"**{finding.Severity} — {finding.Category}: {title}**\n\n{details}\n\n**Why it matters:** {finding.WhyItMatters ?? "See the changed code."}\n\n**Suggested fix:** {finding.SuggestedFix ?? "Review and address this finding."}";
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repository}/issues/{number}/comments")
            { Content = new StringContent(JsonSerializer.Serialize(new { body }), System.Text.Encoding.UTF8, "application/json") };
            request.Headers.UserAgent.ParseAdd("LMMs.Api");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var result = await httpClient.SendAsync(request, cancellationToken);
            if (!result.IsSuccessStatusCode) throw new InvalidOperationException("The review finding could not be posted to the pull request.");
        }
    }
}

public sealed class AzureDevOpsPullRequestProvider : IPullRequestProvider
{
    public string Name => "AzureDevOps";
    public bool CanHandle(Uri url) => url.Host.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase) || url.Host.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase);
    public Task<PullRequestData> GetAsync(Uri url, CancellationToken cancellationToken) => throw new NotSupportedException("Azure DevOps pull request retrieval is not configured yet.");
    public Task AddFindingCommentsAsync(Uri url, IReadOnlyList<ReviewFinding> findings, CancellationToken cancellationToken) => throw new NotSupportedException("Azure DevOps pull request comments are not configured yet.");
}
