using System.Text;
using System.Text.Json.Serialization;
using LMMs.Api.Agents;
using LMMs.Api.Application.Abstractions;
using LMMs.Api.Application.Services;
using LMMs.Api.Domain.Entities;
using LMMs.Api.Infrastructure.Authentication;
using LMMs.Api.Infrastructure.Persistence;
using LMMs.Api.Infrastructure.Persistence.Repositories;
using LMMs.Api.Interfaces;
using LMMs.Api.Planning;
using LMMs.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("LMMsDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:LMMsDatabase must be configured.");
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwtOptions.Issuer) ||
    string.IsNullOrWhiteSpace(jwtOptions.Audience) ||
    string.IsNullOrWhiteSpace(jwtOptions.SigningKey) ||
    jwtOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException(
        "Authentication:Jwt Issuer, Audience, and a SigningKey of at least 32 characters must be configured.");
}

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy => policy
        .WithOrigins("http://localhost:4200", "https://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var ollamaEndpoint = builder.Configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
var ollamaModel = builder.Configuration["Ollama:Model"] ?? "llama3.1";
builder.Services.AddChatClient(new OllamaChatClient(new Uri(ollamaEndpoint), ollamaModel)).UseFunctionInvocation();

builder.Services.AddHttpClient();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
builder.Services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
builder.Services.AddScoped<IToolExecutionRepository, ToolExecutionRepository>();
builder.Services.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IConversationTurnService, ConversationTurnService>();

builder.Services.AddScoped<ILLMSDecision, LLMSDecision>();
builder.Services.AddScoped<IToolInvoker, AgentToolInvoker>();
builder.Services.AddScoped<IPlanner, ChatClientPlanner>();
builder.Services.AddScoped<IPlanExecutor, SequentialPlanExecutor>();
builder.Services.AddScoped<PlanningAgent>();
builder.Services.AddScoped<IAgent>(provider => provider.GetRequiredService<PlanningAgent>());
builder.Services.AddScoped<IAgentTurnRunner>(provider => provider.GetRequiredService<PlanningAgent>());
builder.Services.AddScoped<IAgentAnswer, AgentAnswer>();
builder.Services.AddScoped<FileContext>();

builder.Services.AddScoped<IAgentTool, CalculatorTool>();
builder.Services.AddScoped<IAgentTool, GetTimeTool>();
builder.Services.AddScoped<IAgentTool, GetDateTool>();
builder.Services.AddScoped<IAgentTool, FileReaderTool>();
builder.Services.AddScoped<IAgentTool, WebSearchTool>();
builder.Services.AddScoped<ITestInterface, TestInterface>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
