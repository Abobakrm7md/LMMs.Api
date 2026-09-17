using LMMs.Api.Interfaces;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace LMMs.Api.Services
{
    // nuget: Microsoft.Extensions.Http
    public class WebSearchTool : IAgentTool
    {
        private readonly HttpClient _http;
        private readonly string _apiKey; // من SerpApi أو Brave Search API

        public WebSearchTool(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = "01eaecb91f2baa53705423793d46ea45655b50b8";
        }

        public string Name => "web_search";

        public Delegate GetFunction() => Search;

        // الـ LLM هيشوف الـ description ده ويقرر امتى يستخدم الـ tool
        [Description("Search the web for current information. Use when asked about recent events or facts you don't know.")]
        private async Task<string> Search(
            [Description("The search query")] string query)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://google.serper.dev/search");
            request.Headers.Add("X-API-KEY", _apiKey);
            var content = new StringContent($"{{\"q\":\"{query}\"}}", null, "application/json");
            request.Content = content;
            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            return ParseSerperResponse(data);
        }
        private static string ParseSerperResponse(JsonElement data)
        {
            var sb = new StringBuilder();

            // 1. Answer Box — لو موجود بيكون أدق إجابة
            if (data.TryGetProperty("answerBox", out var answerBox))
            {
                if (answerBox.TryGetProperty("answer", out var answer))
                    sb.AppendLine($"Direct Answer: {answer.GetString()}");

                else if (answerBox.TryGetProperty("snippet", out var snippet))
                    sb.AppendLine($"Direct Answer: {snippet.GetString()}");

                sb.AppendLine();
            }

            // 2. Knowledge Graph — معلومات عامة عن الموضوع
            if (data.TryGetProperty("knowledgeGraph", out var kg))
            {
                if (kg.TryGetProperty("title", out var title))
                    sb.AppendLine($"About: {title.GetString()}");

                if (kg.TryGetProperty("description", out var desc))
                    sb.AppendLine($"Description: {desc.GetString()}");

                sb.AppendLine();
            }

            // 3. Organic Results — النتايج الرئيسية
            if (data.TryGetProperty("organic", out var organic))
            {
                sb.AppendLine("Search Results:");
                sb.AppendLine(new string('-', 40));

                foreach (var result in organic.EnumerateArray().Take(3))
                {
                    if (result.TryGetProperty("title", out var t))
                        sb.AppendLine($"Title: {t.GetString()}");

                    if (result.TryGetProperty("snippet", out var s))
                        sb.AppendLine($"Summary: {s.GetString()}");

                    if (result.TryGetProperty("link", out var l))
                        sb.AppendLine($"URL: {l.GetString()}");

                    sb.AppendLine();
                }
            }
            Console.WriteLine(sb.ToString());
            return sb.Length > 0 ? sb.ToString() : "No results found.";
        }

     
    }
}
