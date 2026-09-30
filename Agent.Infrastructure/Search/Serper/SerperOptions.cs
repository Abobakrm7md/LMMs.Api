namespace Agent.Infrastructure.Search.Serper;
public sealed class SerperOptions
{
    public const string SectionName = "Search:Serper";
    public string Endpoint { get; set; } = "https://google.serper.dev/search";
    public string ApiKey { get; set; } = string.Empty;
}
