using Agent.Application.Agents;
using Agent.Application.Authentication;
using Agent.Application.Conversations;
using Agent.Application.Execution;
using Agent.Application.Persistence;
using Agent.Application.Planning;
using Agent.Application.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Agent.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentApplication(this IServiceCollection services)
    {
        services.AddScoped<IPlanner, ChatModelPlanner>();
        services.AddScoped<IAgentExecutor, AgentExecutor>();
        services.AddScoped<IToolRegistry, ToolRegistry>();
        services.AddScoped<PlanningAgent>();
        services.AddScoped<FileAnalysisAgent>();
        services.AddScoped<ISpecializedAgent>(provider => provider.GetRequiredService<FileAnalysisAgent>());
        services.AddScoped<ISpecializedAgent>(provider => provider.GetRequiredService<PlanningAgent>());
        services.AddScoped<IAgentRegistry, AgentRegistry>();
        services.AddScoped<IAgentTurnRunner, SupervisorAgent>();
        services.AddScoped<IAgent>(provider => provider.GetRequiredService<PlanningAgent>());
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IConversationTurnService, ConversationTurnService>();
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}
