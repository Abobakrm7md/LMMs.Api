using System.ComponentModel;
using System.Runtime.CompilerServices;
using LMMs.Api.Interfaces;
using LMMs.Api.Planning;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LMMs.Api.Tests
{
    public sealed class SequentialPlanExecutorTests
    {
        [Fact]
        public async Task NoTools_CompletesWithoutInvocation()
        {
            var invoker = new RecordingToolInvoker();
            var executor = CreateExecutor(invoker, new ScriptedPlanner());

            var result = await executor.ExecuteAsync(
                AgentPlan.Direct("greet the user"),
                [],
                "hello",
                CancellationToken.None);

            Assert.True(result.Completed);
            Assert.Empty(result.StepResults);
            Assert.Empty(invoker.Calls);
        }

        [Fact]
        public async Task SingleTool_ExecutesOnce()
        {
            var invoker = new RecordingToolInvoker
            {
                Handler = (_, _) => ToolExecutionResult.Success(1, "time", "GetTime", new Dictionary<string, object?>(), "12:00:00")
            };
            var executor = CreateExecutor(invoker, new ScriptedPlanner());
            var plan = new AgentPlan
            {
                Goal = "Get the current time",
                Kind = PlanKind.SingleTool,
                Steps =
                [
                    new PlanStep
                    {
                        Id = 1,
                        Description = "Get time",
                        Tool = "GetTime",
                        Input = new Dictionary<string, object?>()
                    }
                ]
            };

            var result = await executor.ExecuteAsync(plan, [new NamedTool("GetTime")], "what time is it?", CancellationToken.None);

            Assert.True(result.Completed);
            Assert.Single(result.StepResults);
            Assert.Equal("12:00:00", result.StepResults[0].Output);
            Assert.Single(invoker.Calls);
        }

        [Fact]
        public async Task MultiStep_PassesPriorResultIntoNextInput()
        {
            var invoker = new RecordingToolInvoker
            {
                Handler = (step, _) =>
                {
                    if (step.Tool == "GetDate")
                        return ToolExecutionResult.Success(step.Id, step.Description, step.Tool, step.Input, "2026-09-17");

                    var expr = step.Input["input"]?.ToString();
                    return ToolExecutionResult.Success(step.Id, step.Description, step.Tool, step.Input, expr ?? string.Empty);
                }
            };
            var executor = CreateExecutor(invoker, new ScriptedPlanner());
            var plan = new AgentPlan
            {
                Goal = "Use the date then calculate",
                Kind = PlanKind.MultiStep,
                Steps =
                [
                    new PlanStep
                    {
                        Id = 1,
                        Description = "Get date",
                        Tool = "GetDate",
                        Input = new Dictionary<string, object?>()
                    },
                    new PlanStep
                    {
                        Id = 2,
                        Description = "Calculate",
                        Tool = "Calculator",
                        Input = new Dictionary<string, object?> { ["input"] = "{{1}} + 1" }
                    }
                ]
            };

            var result = await executor.ExecuteAsync(
                plan,
                [new NamedTool("GetDate"), new NamedTool("Calculator")],
                "use the date",
                CancellationToken.None);

            Assert.True(result.Completed);
            Assert.Equal(2, result.StepResults.Count);
            Assert.Equal("17 + 1", result.StepResults[1].Output);
        }

        [Fact]
        public async Task CalculatorNamedPlaceholders_BecomeMathExpression()
        {
            var invoker = new RecordingToolInvoker
            {
                Handler = (step, _) =>
                {
                    if (string.Equals(step.Tool, "GetDate", StringComparison.OrdinalIgnoreCase))
                        return ToolExecutionResult.Success(step.Id, step.Description, step.Tool, step.Input, "2026-09-17");

                    var expr = step.Input.TryGetValue("input", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
                    var computed = new System.Data.DataTable().Compute(expr, null)?.ToString() ?? expr;
                    return ToolExecutionResult.Success(step.Id, step.Description, step.Tool, step.Input, computed);
                }
            };
            var executor = CreateExecutor(invoker, new ScriptedPlanner());
            var plan = new AgentPlan
            {
                Goal = "Multiply then add the day of month",
                Kind = PlanKind.MultiStep,
                Steps =
                [
                    new PlanStep
                    {
                        Id = 1,
                        Description = "Multiply 25 * 4",
                        Tool = "Calculator",
                        Input = new Dictionary<string, object?> { ["input"] = "25 * 4" }
                    },
                    new PlanStep
                    {
                        Id = 2,
                        Description = "Get today's date",
                        Tool = "GetDate",
                        Input = new Dictionary<string, object?>()
                    },
                    new PlanStep
                    {
                        Id = 3,
                        Description = "Add the day of month to the result",
                        Tool = "Calculator",
                        Input = new Dictionary<string, object?>
                        {
                            ["result"] = "step1.result",
                            ["day_of_month"] = "step2"
                        }
                    }
                ]
            };

            var result = await executor.ExecuteAsync(
                plan,
                [new NamedTool("Calculator"), new NamedTool("GetDate")],
                "What is 25 × 4? Then get today's date and add the day of the month to the result.",
                CancellationToken.None);

            Assert.True(result.Completed);
            Assert.Equal(3, result.StepResults.Count);
            Assert.Equal("117", result.StepResults[2].Output);
            Assert.Equal("100 + 17", invoker.Calls[2].Input["input"]?.ToString());
        }

        [Fact]
        public async Task ToolFailure_AbortsWhenPlannerAborts()
        {
            var invoker = new RecordingToolInvoker
            {
                Handler = (step, _) => ToolExecutionResult.Failure(step.Id, step.Description, step.Tool, step.Input, "boom")
            };
            var planner = new ScriptedPlanner { Next = ExecutionDirective.Abort("tool failed") };
            var executor = CreateExecutor(invoker, planner);
            var plan = SingleCalculatorPlan();

            var result = await executor.ExecuteAsync(plan, [new NamedTool("Calculator")], "1+1", CancellationToken.None);

            Assert.True(result.Aborted);
            Assert.False(result.Completed);
            Assert.Single(result.StepResults);
            Assert.False(result.StepResults[0].Succeeded);
        }

        [Fact]
        public async Task InvalidTool_IsAFailure()
        {
            var invoker = new AgentToolInvoker(NullLogger<AgentToolInvoker>.Instance);
            var planner = new ScriptedPlanner { Next = ExecutionDirective.Abort("unknown tool") };
            var executor = CreateExecutor(invoker, planner);
            var plan = new AgentPlan
            {
                Goal = "call missing tool",
                Kind = PlanKind.SingleTool,
                Steps =
                [
                    new PlanStep
                    {
                        Id = 1,
                        Description = "Missing",
                        Tool = "does_not_exist",
                        Input = new Dictionary<string, object?>()
                    }
                ]
            };

            var result = await executor.ExecuteAsync(plan, [new NamedTool("Calculator")], "go", CancellationToken.None);

            Assert.True(result.Aborted);
            Assert.Contains("Unknown tool", result.StepResults[0].Error ?? string.Empty);
        }

        [Fact]
        public async Task EmptyToolResult_IsTreatedAsFailure()
        {
            var invoker = new RecordingToolInvoker
            {
                Handler = (step, _) => ToolExecutionResult.Success(step.Id, step.Description, step.Tool, step.Input, "   ")
            };
            var planner = new ScriptedPlanner { Next = ExecutionDirective.Abort("empty") };
            var executor = CreateExecutor(invoker, planner);

            var result = await executor.ExecuteAsync(SingleCalculatorPlan(), [new NamedTool("Calculator")], "calc", CancellationToken.None);

            Assert.True(result.Aborted);
            Assert.True(result.StepResults[0].IsEmpty);
            Assert.False(result.StepResults[0].Succeeded);
        }

        [Fact]
        public async Task Cancellation_Throws()
        {
            var invoker = new RecordingToolInvoker();
            var executor = CreateExecutor(invoker, new ScriptedPlanner());
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                executor.ExecuteAsync(SingleCalculatorPlan(), [new NamedTool("Calculator")], "calc", cts.Token));
        }

        private static SequentialPlanExecutor CreateExecutor(IToolInvoker invoker, IPlanner planner) =>
            new(invoker, planner, NullLogger<SequentialPlanExecutor>.Instance);

        private static AgentPlan SingleCalculatorPlan() => new()
        {
            Goal = "Calculate",
            Kind = PlanKind.SingleTool,
            Steps =
            [
                new PlanStep
                {
                    Id = 1,
                    Description = "Add",
                    Tool = "Calculator",
                    Input = new Dictionary<string, object?> { ["input"] = "1+1" }
                }
            ]
        };
    }

    public sealed class PlanStepResolverTests
    {
        [Fact]
        public void CalculatorJsonStringInput_BecomesExpression()
        {
            var prior = new[]
            {
                ToolExecutionResult.Success(1, "Multiply", "Calculator", new Dictionary<string, object?>(), "100"),
                ToolExecutionResult.Success(2, "Date", "GetDate", new Dictionary<string, object?>(), "2026-09-17")
            };

            var resolved = PlanStepResolver.Resolve(new PlanStep
            {
                Id = 3,
                Description = "Add the day of month",
                Tool = "Calculator",
                Input = new Dictionary<string, object?>
                {
                    ["input"] = """{"result": "step1.result", "day_of_month": "step2"}"""
                }
            }, prior);

            Assert.Equal("100 + 17", resolved.Input["input"]?.ToString());
            Assert.False(PlanStepResolver.NeedsCalculatorRewrite(resolved));
        }
    }

    public sealed class PlanJsonParserTests
    {
        [Fact]
        public void ParsesMultiStepPlan()
        {
            var json = """
                ```json
                {
                  "goal": "Calculate using retrieved data",
                  "kind": "multi_step",
                  "steps": [
                    { "id": 1, "description": "Get date", "tool": "GetDate", "input": {} },
                    { "id": 2, "description": "Calculate", "tool": "Calculator", "input": { "input": "{{1}}" } }
                  ]
                }
                ```
                """;

            var plan = PlanJsonParser.ParsePlan(json);

            Assert.Equal(PlanKind.MultiStep, plan.Kind);
            Assert.Equal(2, plan.Steps.Count);
            Assert.Equal("GetDate", plan.Steps[0].Tool);
        }

        [Fact]
        public void DirectActionJson_BecomesDirectPlan()
        {
            var plan = PlanJsonParser.ParsePlan("""{ "action": "answer" }""");
            Assert.Equal(PlanKind.DirectAnswer, plan.Kind);
            Assert.Empty(plan.Steps);
        }
    }

    public sealed class AgentToolInvokerTests
    {
        [Fact]
        public async Task Calculator_ComputesExpression()
        {
            var invoker = new AgentToolInvoker(NullLogger<AgentToolInvoker>.Instance);
            var step = new PlanStep
            {
                Id = 1,
                Description = "math",
                Tool = "calculator",
                Input = new Dictionary<string, object?> { ["input"] = "5 * (10 + 2)" }
            };

            var result = await invoker.InvokeAsync(step, [new LMMs.Api.Services.CalculatorTool()], CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("60", result.Output);
        }
    }

    public sealed class PlanningAgentTests
    {
        [Fact]
        public async Task DirectAnswer_StreamsFinalTextWithoutTools()
        {
            var chat = new FakeChatClient { StreamingText = "Hello there" };
            var planner = new ScriptedPlanner { Plan = AgentPlan.Direct("greet") };
            var executor = new SequentialPlanExecutor(
                new RecordingToolInvoker(), planner, NullLogger<SequentialPlanExecutor>.Instance);
            var agent = new LMMs.Api.Services.PlanningAgent(
                chat,
                planner,
                executor,
                [],
                new LMMs.Api.Services.FileContext(),
                NullLogger<LMMs.Api.Services.PlanningAgent>.Instance);

            var chunks = new List<string>();
            await foreach (var piece in agent.RunAsync(
                               new LMMs.Api.ViewModels.ChatRequest { Prompt = "hi", EnableTools = false },
                               [],
                               CancellationToken.None))
            {
                chunks.Add(piece);
            }

            Assert.Equal("Hello there", string.Join(string.Empty, chunks));
            Assert.Empty(chat.ResponseCalls);
        }

        [Fact]
        public async Task SingleTool_IncludesToolResultInFinalAnswerContext()
        {
            var chat = new FakeChatClient { StreamingText = "It is 12:00:00" };
            var planner = new ScriptedPlanner
            {
                Plan = new AgentPlan
                {
                    Goal = "Get the time",
                    Kind = PlanKind.SingleTool,
                    Steps =
                    [
                        new PlanStep
                        {
                            Id = 1,
                            Description = "Get time",
                            Tool = "GetTime",
                            Input = new Dictionary<string, object?>()
                        }
                    ]
                }
            };
            var invoker = new RecordingToolInvoker
            {
                Handler = (step, _) => ToolExecutionResult.Success(step.Id, step.Description, step.Tool, step.Input, "12:00:00")
            };
            var agent = CreateAgent(chat, planner, invoker, new NamedTool("GetTime"));

            var text = await ReadAllAsync(agent, new LMMs.Api.ViewModels.ChatRequest
            {
                Prompt = "what time is it?",
                EnableTools = true
            });

            Assert.Equal("It is 12:00:00", text);
            Assert.Single(invoker.Calls);
        }

        [Fact]
        public async Task MultiStep_ExecutesSequentialToolsThenAnswers()
        {
            var chat = new FakeChatClient { StreamingText = "The value is 10" };
            var planner = new ScriptedPlanner
            {
                Plan = new AgentPlan
                {
                    Goal = "Get date then calculate",
                    Kind = PlanKind.MultiStep,
                    Steps =
                    [
                        new PlanStep { Id = 1, Description = "Date", Tool = "GetDate", Input = new Dictionary<string, object?>() },
                        new PlanStep
                        {
                            Id = 2,
                            Description = "Calc",
                            Tool = "Calculator",
                            Input = new Dictionary<string, object?> { ["input"] = "5+5" }
                        }
                    ]
                }
            };
            var invoker = new RecordingToolInvoker
            {
                Handler = (step, _) => ToolExecutionResult.Success(
                    step.Id, step.Description, step.Tool, step.Input, step.Tool == "GetDate" ? "2026-09-17" : "10")
            };
            var agent = CreateAgent(chat, planner, invoker, new NamedTool("GetDate"), new NamedTool("Calculator"));

            var text = await ReadAllAsync(agent, new LMMs.Api.ViewModels.ChatRequest
            {
                Prompt = "get today's date then add 5+5",
                EnableTools = true
            });

            Assert.Equal("The value is 10", text);
            Assert.Equal(2, invoker.Calls.Count);
        }

        [Fact]
        public async Task Cancellation_StopsTheAgent()
        {
            var chat = new FakeChatClient { StreamingText = "should not appear" };
            var planner = new CancellingPlanner();
            var executor = new SequentialPlanExecutor(
                new RecordingToolInvoker(), planner, NullLogger<SequentialPlanExecutor>.Instance);
            var agent = new LMMs.Api.Services.PlanningAgent(
                chat, planner, executor, [], new LMMs.Api.Services.FileContext(),
                NullLogger<LMMs.Api.Services.PlanningAgent>.Instance);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in agent.RunAsync(
                                   new LMMs.Api.ViewModels.ChatRequest { Prompt = "hi", EnableTools = true },
                                   [],
                                   cts.Token))
                {
                }
            });
        }

        private static LMMs.Api.Services.PlanningAgent CreateAgent(
            IChatClient chat,
            IPlanner planner,
            IToolInvoker invoker,
            params IAgentTool[] tools)
        {
            var executor = new SequentialPlanExecutor(invoker, planner, NullLogger<SequentialPlanExecutor>.Instance);
            return new LMMs.Api.Services.PlanningAgent(
                chat,
                planner,
                executor,
                tools,
                new LMMs.Api.Services.FileContext(),
                NullLogger<LMMs.Api.Services.PlanningAgent>.Instance);
        }

        private static async Task<string> ReadAllAsync(
            LMMs.Api.Services.PlanningAgent agent,
            LMMs.Api.ViewModels.ChatRequest request)
        {
            var chunks = new List<string>();
            await foreach (var piece in agent.RunAsync(request, [], CancellationToken.None))
                chunks.Add(piece);
            return string.Join(string.Empty, chunks);
        }
    }

    public sealed class ChatClientPlannerTests
    {
        [Fact]
        public async Task CreatePlanAsync_WithNoTools_DoesNotCallChat()
        {
            var chat = new FakeChatClient();
            var planner = new ChatClientPlanner(chat, NullLogger<ChatClientPlanner>.Instance);

            var plan = await planner.CreatePlanAsync(new PlanningRequest
            {
                UserPrompt = "hello",
                Tools = []
            }, CancellationToken.None);

            Assert.Equal(PlanKind.DirectAnswer, plan.Kind);
            Assert.Empty(chat.ResponseCalls);
        }

        [Fact]
        public async Task CreatePlanAsync_ParsesModelJson()
        {
            var chat = new FakeChatClient
            {
                ResponseText = """{"goal":"add","kind":"single_tool","steps":[{"id":1,"description":"calc","tool":"Calculator","input":{"input":"1+1"}}]}"""
            };
            var planner = new ChatClientPlanner(chat, NullLogger<ChatClientPlanner>.Instance);

            var plan = await planner.CreatePlanAsync(new PlanningRequest
            {
                UserPrompt = "1+1",
                Tools = [new ToolDescriptor { Name = "Calculator", Description = "math", Parameters = ["input"] }]
            }, CancellationToken.None);

            Assert.Equal(PlanKind.SingleTool, plan.Kind);
            Assert.Equal("Calculator", plan.Steps[0].Tool);
            Assert.Single(chat.ResponseCalls);
        }

        [Fact]
        public async Task CreatePlanAsync_HonorsCancellation()
        {
            var planner = new ChatClientPlanner(new FakeChatClient(), NullLogger<ChatClientPlanner>.Instance);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                planner.CreatePlanAsync(new PlanningRequest
                {
                    UserPrompt = "1+1",
                    Tools = [new ToolDescriptor { Name = "Calculator", Description = "math", Parameters = ["input"] }]
                }, cts.Token));
        }
    }

    file sealed class NamedTool : IAgentTool
    {
        public NamedTool(string name) => Name = name;
        public string Name { get; }
        public Delegate GetFunction() => (Func<string>)(() => Name);
    }

    file sealed class RecordingToolInvoker : IToolInvoker
    {
        public List<PlanStep> Calls { get; } = [];
        public Func<PlanStep, IReadOnlyList<IAgentTool>, ToolExecutionResult>? Handler { get; set; }

        public Task<ToolExecutionResult> InvokeAsync(
            PlanStep step,
            IReadOnlyList<IAgentTool> tools,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(step);
            if (Handler is not null)
                return Task.FromResult(Handler(step, tools));

            return Task.FromResult(ToolExecutionResult.Success(step.Id, step.Description, step.Tool, step.Input, "ok"));
        }
    }

    file sealed class ScriptedPlanner : IPlanner
    {
        public AgentPlan Plan { get; set; } = AgentPlan.Direct("direct");
        public ExecutionDirective Next { get; set; } = ExecutionDirective.Abort("default abort");

        public Task<AgentPlan> CreatePlanAsync(PlanningRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Plan);
        }

        public Task<ExecutionDirective> DecideNextAsync(ExecutionState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Next);
        }
    }

    file sealed class CancellingPlanner : IPlanner
    {
        public Task<AgentPlan> CreatePlanAsync(PlanningRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AgentPlan.Direct("x"));
        }

        public Task<ExecutionDirective> DecideNextAsync(ExecutionState state, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ExecutionDirective.Complete("done"));
        }
    }

    file sealed class FakeChatClient : IChatClient
    {
        public string StreamingText { get; set; } = "ok";
        public string ResponseText { get; set; } = "ok";
        public List<IReadOnlyList<ChatMessage>> ResponseCalls { get; } = [];

        public ChatClientMetadata Metadata { get; } = new("fake");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResponseCalls.Add(chatMessages.ToList());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(StreamingText)]
            };
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
