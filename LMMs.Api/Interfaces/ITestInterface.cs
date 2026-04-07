using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;

namespace LMMs.Api.Interfaces
{
    public interface ITestInterface
    {
        IAsyncEnumerable<string> RunAgentUsingAIFunctions(string userPrompt, List<ChatMessage> chatMessages, CancellationToken cancellationToken);
        List<ChatMessage> BuildMessagesToSend(List<ChatMessage> chatMessages);
    }
    public class TestInterface : ITestInterface
    {
        private readonly IChatClient _chatClient;
        private readonly IReadOnlyList<IAgentTool> _tools;
        private readonly AITool[] _cachedTools;

        public TestInterface(IChatClient chatClient, IEnumerable<IAgentTool> tools)
        {
            _chatClient = chatClient;
            _tools = tools.ToList();

            _cachedTools = _tools
                .Select(t => AIFunctionFactory.Create(t.GetFunction()))
                .ToArray();
        }

        public async IAsyncEnumerable<string> RunAgentUsingAIFunctions(
            string userPrompt,
            List<ChatMessage> chatMessages,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            chatMessages.Add(new ChatMessage(Microsoft.Extensions.AI.ChatRole.User, userPrompt));

            var options = new ChatOptions
            {
                Tools = _cachedTools,
                ToolMode = ChatToolMode.Auto,
                Temperature = 0.1f
            };

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var messagesToSend = BuildMessagesToSend(chatMessages);
                var fullResponse = new StringBuilder();
                var toolCalls = new List<FunctionCallContent>();

                await foreach (var chunk in _chatClient
                    .GetStreamingResponseAsync(messagesToSend, options, cancellationToken)
                    .WithCancellation(cancellationToken))
                {
                    if (chunk.Text is not null)
                    {
                        fullResponse.Append(chunk.Text);
                        yield return chunk.Text;
                    }

                    toolCalls.AddRange(chunk.Contents.OfType<FunctionCallContent>());
                }

                if (fullResponse.Length > 0)
                    chatMessages.Add(new ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, fullResponse.ToString()));

                if (toolCalls.Count == 0) break;

                // ✅ نفّذ كل tool
                foreach (var call in toolCalls)
                {
                    var result = await ExecuteToolCallAsync(call, cancellationToken);

                    // ✅ FunctionResultContent بـ 2 arguments بس
                    chatMessages.Add(new ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Tool,
                        [new FunctionResultContent(call.CallId, result)]
                    ));
                }
            }
        }

        private async Task<string> ExecuteToolCallAsync(
            FunctionCallContent call,
            CancellationToken cancellationToken)
        {
            // ✅ ابحث في الـ IAgentTool الأصلية بالاسم
            var tool = _tools.FirstOrDefault(t =>
                t.GetFunction().Method.Name.Equals(call.Name, StringComparison.OrdinalIgnoreCase));

            if (tool is null)
                return $"Tool '{call.Name}' not found.";

            try
            {
                // نفّذ الـ function مباشرة من الـ IAgentTool
                var methodResult = await Task.Run(
                    () => tool.GetFunction().DynamicInvoke(
                        ResolveArguments(tool.GetFunction().Method, call.Arguments)
                    ), cancellationToken);

                return methodResult?.ToString() ?? "Done.";
            }
            catch (Exception ex)
            {
                // ارجع الـ error للـ LLM يتعامل معاه بدل ما يكرش
                return $"Tool error: {ex.Message}";
            }
        }

        private static object?[] ResolveArguments(
            System.Reflection.MethodInfo method,
            IDictionary<string, object?>? args)
        {
            if (args is null) return [];

            return method.GetParameters()
                .Select(p => args.TryGetValue(p.Name!, out var val) ? val : null)
                .ToArray();
        }
        public List<ChatMessage> BuildMessagesToSend(List<ChatMessage> chatMessages)
        {
            var result = new List<ChatMessage>
        {
            new(ChatRole.System, GetSystemPrompt())
        };

            const int Budget = 5000;
            int used = 0;
            var selected = new List<ChatMessage>();

            foreach (var msg in chatMessages.AsEnumerable().Reverse())
            {
                int tokens = EstimateTokens(msg);
                if (used + tokens > Budget) break;
                selected.Insert(0, msg);
                used += tokens;
            }

            result.AddRange(selected);
            return result;
        }

        private static int EstimateTokens(ChatMessage msg) =>
            msg.Contents.OfType<TextContent>().Sum(c => c.Text?.Length ?? 0) / 4 + 4;

        private static string GetSystemPrompt() =>
             @"You are a helpful assistant.

                    When you use a tool:
                    - DO NOT show the tool call
                    - DO NOT return JSON
                    - DO NOT explain the process

                    ONLY return the final human-readable answer.";
    }
    // باقي الكود زي ما هو...

}
