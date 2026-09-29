using Agent.Application.Chat;
using Agent.Application.Conversations;
using Agent.Application.Files;
using Agent.Application.Search;
using Agent.Application.Tools;
using Agent.Infrastructure.AI.Ollama;
using Agent.Infrastructure.Conversations;
using Agent.Infrastructure.Files;
using Agent.Infrastructure.Search.Serper;
using Agent.Infrastructure.Tools.Calculator;
using Agent.Infrastructure.Tools.DateTime;
using Agent.Infrastructure.Tools.Files;
using Agent.Infrastructure.Tools.WebSearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var ollama = new OllamaOptions();
        configuration.GetSection(OllamaOptions.SectionName).Bind(ollama);
        services.AddChatClient(new OllamaChatClient(new Uri(ollama.Endpoint), ollama.Model));
        services.AddScoped<IChatModel, OllamaChatModel>();

        var storage = new AttachmentStorageOptions();
        configuration.GetSection("Files").Bind(storage);
        services.AddSingleton(storage);
        services.AddScoped<IAttachmentStore, LocalAttachmentStore>();
        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        services.AddSingleton(TimeProvider.System);

        var serper = new SerperOptions();
        configuration.GetSection(SerperOptions.SectionName).Bind(serper);
        services.AddSingleton(serper);
        services.AddHttpClient<SerperWebSearchProvider>();
        services.AddScoped<IWebSearchProvider>(provider => provider.GetRequiredService<SerperWebSearchProvider>());

        services.AddScoped<IAgentTool, CalculatorTool>();
        services.AddScoped<IAgentTool, GetDateTool>();
        services.AddScoped<IAgentTool, GetTimeTool>();
        services.AddScoped<IAgentTool, FileReaderTool>();
        services.AddScoped<IAgentTool, WebSearchTool>();
        return services;
    }
}
