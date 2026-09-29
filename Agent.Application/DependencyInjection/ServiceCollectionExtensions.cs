using Agent.Application.Agents;
using Agent.Application.Execution;
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
        services.AddScoped<IAgent, PlanningAgent>();
        return services;
    }
}
