using LMMs.Api.Agents;
using LMMs.Api.Interfaces;
using LMMs.Api.Planning;
using LMMs.Api.Services;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:4200") // Angular dev server
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});
builder.Services.AddChatClient(new OllamaChatClient(
    new Uri("http://localhost:11434"), "llama3.1")).UseFunctionInvocation();

builder.Services.AddHttpClient();
builder.Services.AddScoped<ILLMSDecision, LLMSDecision>();
builder.Services.AddScoped<IToolInvoker, AgentToolInvoker>();
builder.Services.AddScoped<IPlanner, ChatClientPlanner>();
builder.Services.AddScoped<IPlanExecutor, SequentialPlanExecutor>();
builder.Services.AddScoped<IAgent, PlanningAgent>();
builder.Services.AddScoped<IAgentAnswer, AgentAnswer>();
builder.Services.AddScoped<FileContext>();

// IAgentTool must be Scoped (not Singleton): FileReaderTool consumes scoped FileContext per request.
builder.Services.AddScoped<IAgentTool, CalculatorTool>();
builder.Services.AddScoped<IAgentTool, GetTimeTool>();
builder.Services.AddScoped<IAgentTool, GetDateTool>();
builder.Services.AddScoped<IAgentTool, FileReaderTool>();
builder.Services.AddScoped<IAgentTool, WebSearchTool>();
builder.Services.AddScoped<ITestInterface, TestInterface>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();

app.Run();


//record ChatRequest(string Prompt);