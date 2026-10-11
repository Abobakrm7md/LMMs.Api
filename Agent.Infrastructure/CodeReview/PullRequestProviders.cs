using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Agent.Application.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Agent.Infrastructure.CodeReview;

public sealed class GitHubPullRequestProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<GitHubPullRequestProvider> logger) : IPullRequestProvider
{
    private const string ApiVersion = "2022-11-28";
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    public string Name => "GitHub";

    public bool CanHandle(Uri url) =>
        url.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 4;

    public async Task<PullRequestData> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        var (repository, number) = ParseUrl(url);

        using var request = CreateRequest(HttpMethod.Get,
            $"https://api.github.com/repos/{repository}/pulls/{number}/files");

        using var response = await SendWithRetryAsync(request, cancellationToken);
        await EnsureSuccessOrThrowAsync(response, "retrieve pull request files", cancellationToken);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var files = json.RootElement.EnumerateArray()
            .Select(x => new PullRequestFile(
                x.GetProperty("filename").GetString() ?? "",
                x.TryGetProperty("patch", out var patch) ? patch.GetString() ?? "" : ""))
            .ToList();

        return new PullRequestData(Name, repository, number, "", files);
    }

    public async Task AddFindingCommentsAsync(
        Uri url,
        IReadOnlyList<ReviewFinding> findings,
        CancellationToken cancellationToken)
    {
        var token = GetRequiredToken();
        var (repository, number) = ParseUrl(url);

        logger.LogInformation("Posting {Count} review comment(s) to {Repo}#{Number}", findings.Count, repository, number);

        foreach (var finding in findings)
        {
            var title = finding.Title ?? finding.Message ?? "Code review finding";
            var details = finding.Description ?? finding.Message ?? "No additional details provided.";
            var body = $"**{finding.Severity} — {finding.Category}: {title}**\n\n" +
                       $"{details}\n\n" +
                       $"**Why it matters:** {finding.WhyItMatters ?? "See the changed code."}\n\n" +
                       $"**Suggested fix:** {finding.SuggestedFix ?? "Review and address this finding."}";

            using var request = CreateRequest(HttpMethod.Post,
                $"https://api.github.com/repos/{repository}/issues/{number}/comments",
                token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { body }),
                System.Text.Encoding.UTF8,
                "application/json");

            using var response = await SendWithRetryAsync(request, cancellationToken);
            await EnsureSuccessOrThrowAsync(response, $"post comment '{title}'", cancellationToken);

            logger.LogInformation("Posted comment: {Title}", title);

            // Respect GitHub secondary rate limits: brief pause between writes
            if (findings.Count > 1)
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static (string Repository, string Number) ParseUrl(Uri url)
    {
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return ($"{parts[0]}/{parts[1]}", parts[3]);
    }

    /// <summary>
    /// Returns the configured token, or throws with a clear diagnostic if missing.
    /// </summary>
    private string GetRequiredToken()
    {
        var token = configuration["GitHub:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "GitHub:Token is not configured. Posting review comments requires authentication. " +
                "Set 'GitHub:Token' in appsettings.json or user-secrets to a Personal Access Token (classic with " +
                "'repo' scope, or fine-grained with 'Pull requests: Read and write' on the target repository). " +
                "See: https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens");
        }
        return token;
    }

    /// <summary>
    /// Builds an HttpRequestMessage with all required GitHub headers.
    /// </summary>
    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string? token = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.UserAgent.ParseAdd("LMMs.Api");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        // Use the provided token (for writes) or fall back to optional config (for reads).
        var effectiveToken = token ?? configuration["GitHub:Token"];
        if (!string.IsNullOrWhiteSpace(effectiveToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effectiveToken);

        return request;
    }

    /// <summary>
    /// Retries on transient 403 (rate-limit / abuse), 429, 5xx with exponential back-off.
    /// Does NOT retry on auth-related 403 (bad scopes / missing permissions).
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (attempt > 0)
            {
                // Clone the request since HttpClient disposes it after SendAsync
                var cloned = await CloneRequestAsync(request);
                var delay = RetryDelays[attempt - 1];

                // Honor Retry-After header if GitHub sent one
                if (response?.Headers.RetryAfter?.Delta is { } retryAfter)
                    delay = retryAfter;

                logger.LogWarning("Retrying GitHub API request (attempt {Attempt}) after {Delay}s", attempt + 1, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                response.Dispose();
                response = await httpClient.SendAsync(cloned, cancellationToken);
            }
            else
            {
                response = await httpClient.SendAsync(request, cancellationToken);
            }

            // Only retry on rate-limit / abuse / server errors
            if (response.StatusCode is HttpStatusCode.TooManyRequests ||
                (response.StatusCode is HttpStatusCode.Forbidden && IsRateLimit(response)) ||
                (int)response.StatusCode >= 500)
            {
                continue;
            }

            break;
        }

        return response!;
    }

    private static bool IsRateLimit(HttpResponseMessage response) =>
        response.Headers.Contains("X-RateLimit-Remaining") &&
        response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault() == "0";

    /// <summary>
    /// Throws with GitHub's actual error message instead of a generic string.
    /// </summary>
    private static async Task EnsureSuccessOrThrowAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        string githubMessage;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            githubMessage = doc.RootElement.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : responseBody;
        }
        catch (JsonException)
        {
            githubMessage = responseBody;
        }

        var headerHint = response.StatusCode == HttpStatusCode.Forbidden
            ? " Check that your GitHub:Token has the 'repo' scope (classic PAT) or 'Pull requests: Read and write' (fine-grained PAT), " +
              "and that it has access to the target repository."
            : "";

        throw new InvalidOperationException(
            $"Failed to {operation}: {(int)response.StatusCode} {response.StatusCode} — {githubMessage}{headerHint}");
    }

    /// <summary>
    /// Deep-clones an HttpRequestMessage so we can re-send it after a retry.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (original.Content is not null)
        {
            var body = await original.Content.ReadAsStringAsync();
            clone.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            if (original.Content.Headers.ContentType is { } ct)
                clone.Content.Headers.ContentType = ct;
        }
        return clone;
    }
}

public sealed class AzureDevOpsPullRequestProvider : IPullRequestProvider
{
    public string Name => "AzureDevOps";
    public bool CanHandle(Uri url) => url.Host.Contains("visualstudio.com", StringComparison.OrdinalIgnoreCase) || url.Host.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase);
    public Task<PullRequestData> GetAsync(Uri url, CancellationToken cancellationToken) => throw new NotSupportedException("Azure DevOps pull request retrieval is not configured yet.");
    public Task AddFindingCommentsAsync(Uri url, IReadOnlyList<ReviewFinding> findings, CancellationToken cancellationToken) => throw new NotSupportedException("Azure DevOps pull request comments are not configured yet.");
}
