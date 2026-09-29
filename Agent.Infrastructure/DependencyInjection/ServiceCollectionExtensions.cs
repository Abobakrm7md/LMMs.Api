using System.Text;
using Agent.Application.Authentication;
using Agent.Application.Chat;
using Agent.Application.Conversations;
using Agent.Application.Files;
using Agent.Application.Persistence;
using Agent.Application.Search;
using Agent.Application.Tools;
using Agent.Domain.Persistence;
using Agent.Infrastructure.AI.Ollama;
using Agent.Infrastructure.Authentication;
using Agent.Infrastructure.Conversations;
using Agent.Infrastructure.Files;
using Agent.Infrastructure.Persistence;
using Agent.Infrastructure.Persistence.Repositories;
using Agent.Infrastructure.Search.Serper;
using Agent.Infrastructure.Tools.Calculator;
using Agent.Infrastructure.Tools.DateTime;
using Agent.Infrastructure.Tools.Files;
using Agent.Infrastructure.Tools.WebSearch;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Agent.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAgentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        AddChatAndTools(services, configuration);
        AddPersistenceAndAuthentication(services, configuration);
        return services;
    }

    private static void AddChatAndTools(IServiceCollection services, IConfiguration configuration)
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
    }

    private static void AddPersistenceAndAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LMMsDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:LMMsDatabase must be configured.");

        var jwt = new JwtOptions();
        configuration.GetSection(JwtOptions.SectionName).Bind(jwt);
        if (string.IsNullOrWhiteSpace(jwt.Issuer) || string.IsNullOrWhiteSpace(jwt.Audience) || jwt.SigningKey.Length < 32)
            throw new InvalidOperationException("Authentication:Jwt Issuer, Audience, and a SigningKey of at least 32 characters must be configured.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });
        services.AddAuthorization();
        services.AddHttpContextAccessor();

        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
        services.AddScoped<IToolExecutionRepository, ToolExecutionRepository>();
        services.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
        services.AddScoped<IPasswordService, IdentityPasswordService>();
        services.AddScoped<ITokenService, JwtTokenService>();
    }
}
