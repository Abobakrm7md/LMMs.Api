using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Agent.Application.Search;
namespace Agent.Infrastructure.Search.Serper;

public sealed class SerperWebSearchProvider(HttpClient httpClient, SerperOptions options) : IWebSearchProvider
{
    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey)) return "Web search is not configured.";
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        request.Headers.Add("X-API-KEY", options.ApiKey);
        request.Content = JsonContent.Create(new { q = query });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return Parse(data);
    }

    private static string Parse(JsonElement data)
    {
        var result = new StringBuilder();
        if (data.TryGetProperty("answerBox", out var answerBox))
        {
            if (answerBox.TryGetProperty("answer", out var answer)) result.AppendLine($"Direct Answer: {answer.GetString()}");
            else if (answerBox.TryGetProperty("snippet", out var snippet)) result.AppendLine($"Direct Answer: {snippet.GetString()}");
        }
        if (data.TryGetProperty("knowledgeGraph", out var graph))
        {
            if (graph.TryGetProperty("title", out var title)) result.AppendLine($"About: {title.GetString()}");
            if (graph.TryGetProperty("description", out var description)) result.AppendLine($"Description: {description.GetString()}");
        }
        if (data.TryGetProperty("organic", out var organic))
            foreach (var item in organic.EnumerateArray().Take(3))
            {
                if (item.TryGetProperty("title", out var title)) result.AppendLine($"Title: {title.GetString()}");
                if (item.TryGetProperty("snippet", out var snippet)) result.AppendLine($"Summary: {snippet.GetString()}");
                if (item.TryGetProperty("link", out var link)) result.AppendLine($"URL: {link.GetString()}");
                result.AppendLine();
            }
        return result.Length == 0 ? "No results found." : result.ToString();
    }
}
