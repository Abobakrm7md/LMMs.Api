using System.ComponentModel;
using System.Text;
using System.Text.Json;
using LMMs.Api.Interfaces;

namespace LMMs.Api.Services;

public sealed class WebSearchTool : IAgentTool
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public WebSearchTool(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _apiKey = configuration["Search:SerperApiKey"];
    }

    public string Name => "web_search";
    public Delegate GetFunction() => Search;

    [Description("Search the web for current information. Use when asked about recent events or facts you do not know.")]
    private async Task<string> Search(
        [Description("The search query")] string query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return "Web search is not configured.";
        if (string.IsNullOrWhiteSpace(query) || query.Length > 500)
            return "A web search query must contain 1-500 characters.";

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/search");
        request.Headers.Add("X-API-KEY", _apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { q = query.Trim() }),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return "Web search is temporarily unavailable.";

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = JsonSerializer.Deserialize<JsonElement>(json);
        return ParseSerperResponse(data);
    }

    private static string ParseSerperResponse(JsonElement data)
    {
        var sb = new StringBuilder();
        if (data.TryGetProperty("answerBox", out var answerBox))
        {
            if (answerBox.TryGetProperty("answer", out var answer))
                sb.AppendLine($"Direct Answer: {answer.GetString()}");
            else if (answerBox.TryGetProperty("snippet", out var snippet))
                sb.AppendLine($"Direct Answer: {snippet.GetString()}");
            sb.AppendLine();
        }

        if (data.TryGetProperty("knowledgeGraph", out var knowledgeGraph))
        {
            if (knowledgeGraph.TryGetProperty("title", out var title))
                sb.AppendLine($"About: {title.GetString()}");
            if (knowledgeGraph.TryGetProperty("description", out var description))
                sb.AppendLine($"Description: {description.GetString()}");
            sb.AppendLine();
        }

        if (data.TryGetProperty("organic", out var organic) && organic.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine("Search Results:");
            sb.AppendLine(new string('-', 40));
            foreach (var result in organic.EnumerateArray().Take(3))
            {
                if (result.TryGetProperty("title", out var title))
                    sb.AppendLine($"Title: {title.GetString()}");
                if (result.TryGetProperty("snippet", out var snippet))
                    sb.AppendLine($"Summary: {snippet.GetString()}");
                if (result.TryGetProperty("link", out var link))
                    sb.AppendLine($"URL: {link.GetString()}");
                sb.AppendLine();
            }
        }

        return sb.Length > 0 ? sb.ToString() : "No results found.";
    }
}
