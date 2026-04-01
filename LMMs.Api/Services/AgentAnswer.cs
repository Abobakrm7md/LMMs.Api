using LMMs.Api.Interfaces;
using Microsoft.Extensions.AI;
using System.Data;
using System.Text;
using YourNamespace.Controllers;

namespace LMMs.Api.Services
{
    public class AgentAnswer : IAgentAnswer
    {
        private readonly ILLMSDecision _lLMSDecision;
        private readonly IChatClient _chatClient;
        private readonly IEnumerable<IAgentTool> _tools;

        public AgentAnswer(ILLMSDecision lLMSDecision, IChatClient chatClient, IEnumerable<IAgentTool> tools)
        {
            _lLMSDecision = lLMSDecision;
            _chatClient = chatClient;
            _tools = tools;
        }


        public async IAsyncEnumerable<string> RunAgentUsingAIFunctions(string userPrompt, List<ChatMessage> chatMessages, CancellationToken cancellationToken)
        {
            chatMessages.Add(new ChatMessage(ChatRole.User, userPrompt));

            var options = new ChatOptions
            {
                Tools = _tools
                            .Select(t => AIFunctionFactory.Create(t.GetFunction()))
                            .ToArray(),
                ToolMode = ChatToolMode.Auto,
                Temperature = 0.1f
            };

            var messagesWillSend = chatMessages.TakeLast(3).ToList();
            AddSystemPrompet(messagesWillSend);
            var responseStream = _chatClient.GetStreamingResponseAsync(messagesWillSend, options, cancellationToken);

            StringBuilder fullResponse = new StringBuilder();
            await foreach (var message in responseStream)
            {
                if (message.Text is not null)
                {
                    fullResponse.Append(message.Text);
                    yield return message.Text;
                }
            }

            // add final response in history
            chatMessages.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));
        }
        private void AddSystemPrompet(List<ChatMessage> chatMessages)
        {
            chatMessages.Insert(0, new ChatMessage(ChatRole.System,
                     @"You are a helpful assistant.

                    When you use a tool:
                    - DO NOT show the tool call
                    - DO NOT return JSON
                    - DO NOT explain the process

                    ONLY return the final human-readable answer."));
        }
        #region Manual call and manage loop
        public async Task<string> RunAgent(string userPrompt, List<ChatMessage> chatMessages, HttpResponse httpResponse, CancellationToken cancellationToken)
        {
            #region Old
            var usedTools = new HashSet<string>();
            var context = $"User: {userPrompt}";
            string finalAnswer = "";

            for (int step = 0; step < 5; step++)
            {

                var decision = await _lLMSDecision.GetDecision(context);
                // 🔥 Tool case
                if (decision.action == "tool")
                {
                    if (!usedTools.Contains(decision.tool))
                    {
                        var result = ExecuteTool(decision);
                        finalAnswer += "  **  " + result;
                        context += $"\nTool ({decision.tool}) result: {result}";
                        usedTools.Add(decision.tool);
                    }
                    continue;
                }

                // 🔥 Answer case
                if (decision.action == "answer")
                {
                    var finalrResponse = await GenerateFinalAnswer(chatMessages, httpResponse, cancellationToken);
                    Console.WriteLine("finalrResponse :  " + finalrResponse);

                    return finalrResponse;
                }
            }

            await httpResponse.WriteAsync(finalAnswer);
            Console.WriteLine("finalAnswer :  " + finalAnswer);
            return finalAnswer;

            #endregion
        }

       

        private string ExecuteTool(AgentDecision decision)
        {
            return decision.tool switch
            {
                "calculator" => new DataTable().Compute(decision.input, null).ToString(),
                "time" => DateTime.Now.ToString(),
                "date" => DateTime.Now.Date.ToString(),
                _ => "Unknown tool"
            };
        }
        private async Task<string> GenerateFinalAnswer(List<ChatMessage> _chatMessages, HttpResponse httpResponse, CancellationToken cancellationToken)
        {
            string finalResponse = "";

            await foreach (var item in _chatClient.GetStreamingResponseAsync(
                    _chatMessages, cancellationToken: cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var text = item.Text ?? string.Empty;

                finalResponse += text;

                await httpResponse.WriteAsync(text);
                await httpResponse.Body.FlushAsync();
            }
            return finalResponse;
        }
        #endregion
    }

    //public class MyAgentTools
    //{
    //    [Description("Calculates any math expression like '5 * (10 + 2)'")]
    //    public string Calculator(string expression)
    //        => new DataTable().Compute(expression, null).ToString() ?? "Error";

    //    [Description("Returns the current server time")]
    //    public string GetTime() => DateTime.Now.ToString("T");
    //    [Description("Returns the current server date")]
    //    public string GetDate() => DateTime.Now.ToString();
    //}
}
