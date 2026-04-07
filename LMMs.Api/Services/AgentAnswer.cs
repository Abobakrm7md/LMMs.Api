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
        private readonly AITool[] _cachedTools;
        private readonly FileContext _fileContext; // ✅ مشترك مع الـ Controller

        public AgentAnswer(IChatClient chatClient, IEnumerable<IAgentTool> tools, FileContext fileContext)
        {
            _chatClient = chatClient;
            _tools = tools.ToList();

            _cachedTools = _tools
                .Select(t => AIFunctionFactory.Create(t.GetFunction()))
                .ToArray();
            _fileContext = fileContext;
        }


        public async IAsyncEnumerable<string> RunAgentUsingAIFunctions(ChatRequest request, List<ChatMessage> chatMessages, CancellationToken cancellationToken)
        {
            if (request.File is not null)
            {
                request.Prompt = $"{request.Prompt}\n[There is an attached file: {request.File.FileName}, use read_file tool to read it]";
                await SetFile(request.File, cancellationToken);
            }
            chatMessages.Add(new ChatMessage(ChatRole.User, request.Prompt));

            var options = new ChatOptions
            {
                Tools = _cachedTools,
                ToolMode = ChatToolMode.Auto,
                Temperature = 0.1f
            };

            var messagesWillSend = BuildMessagesToSend(chatMessages);
            //AddSystemPrompet(messagesWillSend);
            var responseStream = _chatClient.GetStreamingResponseAsync(messagesWillSend, options, cancellationToken);

            StringBuilder fullResponse = new StringBuilder();
            await foreach (var message in responseStream.WithCancellation(cancellationToken))
            {
                if (message.Text is not null)
                {
                    fullResponse.Append(message.Text);
                    yield return message.Text;
                }
            }

            // add final response in history
            chatMessages.Add(new ChatMessage(ChatRole.Assistant, fullResponse.ToString()));

            TrimHistory(chatMessages, 5);
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
        private static void TrimHistory(List<ChatMessage> chatMessages, int maxMessages = 20)
        {
            if (chatMessages.Count <= maxMessages) return;

            // احسب كام رسالة هتشيل من الأول
            int toRemove = chatMessages.Count - maxMessages;

            // ✅ ابدأ من index 1 عشان متمسطيش الـ User message الأول
            // وخليك ditch الـ Tool messages مع الـ Assistant اللي قبلها
            int safeRemove = 0;
            for (int i = 0; i < toRemove; i++)
            {
                // متشيلش Tool message لوحدها من غير الـ Assistant اللي قبلها
                if (chatMessages[i].Role == ChatRole.Tool) continue;
                safeRemove = i + 1;
            }

            if (safeRemove > 0)
                chatMessages.RemoveRange(0, safeRemove);
        }
        public async Task SetFile(IFormFile? file, CancellationToken cancellationToken)
        {
            string? savedFileName = null;
            var filesFolder = Path.Combine(Directory.GetCurrentDirectory(), "Files");
            Directory.CreateDirectory(filesFolder); // ينشئها لو مش موجودة

            // ✅ اسم unique عشان متتوورش ملفات بنفس الاسم
            savedFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            var savePath = Path.Combine(filesFolder, savedFileName);

            await using var stream = File.Create(savePath);
            await file.CopyToAsync(stream, cancellationToken);
            _fileContext.FileName = savedFileName;
        }
        //private void AddSystemPrompet(List<ChatMessage> chatMessages)
        //{
        //    chatMessages.Insert(0, new ChatMessage(ChatRole.System,
        //             @"You are a helpful assistant.

        //            When you use a tool:
        //            - DO NOT show the tool call
        //            - DO NOT return JSON
        //            - DO NOT explain the process

        //            ONLY return the final human-readable answer."));
        //}
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
