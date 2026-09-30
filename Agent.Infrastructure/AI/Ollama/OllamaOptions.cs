namespace Agent.Infrastructure.AI.Ollama;
public sealed class OllamaOptions
{
    public const string SectionName = "AI:Ollama";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.1";
}
