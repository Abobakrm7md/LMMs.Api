using LMMs.Api.Controllers;
using LMMs.Api.Interfaces;
using LMMs.Api.ViewModels;
using Microsoft.Extensions.AI;
using System.Data;
using System.Text;

namespace LMMs.Api.Services
{
    public class AgentAnswer : IAgentAnswer
    {
        //private readonly ILLMSDecision _lLMSDecision;
        private readonly IChatClient _chatClient;
        private readonly IReadOnlyList<IAgentTool> _tools;
        private readonly FileContext _fileContext;

        public AgentAnswer(IChatClient chatClient, IEnumerable<IAgentTool> tools, FileContext fileContext)
        {
            _chatClient = chatClient;
            _tools = tools.ToList();
            _fileContext = fileContext;
        }

        private static bool IsWebSearchTool(IAgentTool t) =>
            string.Equals(t.Name, "web_search", StringComparison.OrdinalIgnoreCase);

        private static bool IsFileReaderTool(IAgentTool t) =>
            string.Equals(t.Name, "read_file", StringComparison.OrdinalIgnoreCase);

        private AITool[] GetAiToolsForRequest(ChatRequest request)
        {
            // No tools at all → model can only answer in plain text (fixes "uses a tool every time").
            if (!request.EnableTools && request.File is null)
                return [];

            IEnumerable<IAgentTool> selected = _tools;

            if (!request.EnableTools && request.File is not null)
                selected = selected.Where(IsFileReaderTool);
            else if (request.EnableTools && !request.EnableWebSearch)
                selected = selected.Where(t => !IsWebSearchTool(t));
            else if (request.EnableTools && request.File is null)
                selected = selected.Where(t => !IsFileReaderTool(t));
            return selected.Select(t => AIFunctionFactory.Create(t.GetFunction())).ToArray();
        }


        public async IAsyncEnumerable<string> RunAgentUsingAIFunctions(ChatRequest request, List<ChatMessage> chatMessages, CancellationToken cancellationToken)
        {
            if (request.File is not null)
            {
                request.Prompt =
                    $"{request.Prompt}\n[An optional attachment is available: {request.File.FileName}. Call read_file only if answering requires the file contents.]";
                await SetFile(request.File, cancellationToken);
            }
            chatMessages.Add(new ChatMessage(ChatRole.User, request.Prompt));

            var aiTools = GetAiToolsForRequest(request);
            var options = new ChatOptions
            {
                Tools = aiTools,
                ToolMode = ChatToolMode.Auto,
                Temperature = 0.1f
            };

            var messagesWillSend = BuildMessagesToSend(chatMessages, aiTools.Length > 0);
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
        public List<ChatMessage> BuildMessagesToSend(List<ChatMessage> chatMessages, bool toolsOffered = true)
        {
            var result = new List<ChatMessage>
            {
                new(ChatRole.System, GetSystemPrompt(toolsOffered))
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

        private static string GetSystemPrompt(bool toolsOffered)
        {
            if (!toolsOffered)
            {
                return """
                    You are a helpful assistant.
                    For this message, no tools are available. Answer from your knowledge and the text the user sent.
                    Do not pretend to call tools or search the web.
                    """;
            }

            return """
                You are a helpful assistant.

                Tools are available, but use them only when strictly necessary:
                - Exact current date/time/"what day is today" → time/date tools
                - User needs live web facts and web_search is available → web_search
                - Answering requires the attached file → read_file
                - Exact arithmetic → calculator

                Do not use tools for general chat, explanations, code help, or questions you can answer without tools.

                Never use web_search for stack traces, exceptions, or pasted logs — reason from the text.

                When you use a tool:
                - DO NOT show the tool call
                - DO NOT return JSON
                - DO NOT explain the process

                ONLY return the final human-readable answer.
                """;
        }
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
            Directory.CreateDirectory(filesFolder);

            savedFileName = $"{Guid.NewGuid()}_{Path.GetFileName(file?.FileName)}";
            var savePath = Path.Combine(filesFolder, savedFileName);

            await using var stream = File.Create(savePath);
            await file.CopyToAsync(stream, cancellationToken);
            _fileContext.FileName = savedFileName;
        }

        #region Manual call and manage loop
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
        //public async Task<string> RunAgent(string userPrompt, List<ChatMessage> chatMessages, HttpResponse httpResponse, CancellationToken cancellationToken)
        //{
        //    #region Old
        //    var usedTools = new HashSet<string>();
        //    var context = $"User: {userPrompt}";
        //    string finalAnswer = "";

        //    for (int step = 0; step < 5; step++)
        //    {

        //        var decision = await _lLMSDecision.GetDecision(context);
        //        // 🔥 Tool case
        //        if (decision.action == "tool")
        //        {
        //            if (!usedTools.Contains(decision.tool))
        //            {
        //                var result = ExecuteTool(decision);
        //                finalAnswer += "  **  " + result;
        //                context += $"\nTool ({decision.tool}) result: {result}";
        //                usedTools.Add(decision.tool);
        //            }
        //            continue;
        //        }

        //        // 🔥 Answer case
        //        if (decision.action == "answer")
        //        {
        //            var finalrResponse = await GenerateFinalAnswer(chatMessages, httpResponse, cancellationToken);
        //            Console.WriteLine("finalrResponse :  " + finalrResponse);

        //            return finalrResponse;
        //        }
        //    }

        //    await httpResponse.WriteAsync(finalAnswer);
        //    Console.WriteLine("finalAnswer :  " + finalAnswer);
        //    return finalAnswer;

        //    #endregion
        //}

       

        //private string ExecuteTool(AgentDecision decision)
        //{
        //    return decision.tool switch
        //    {
        //        "calculator" => new DataTable().Compute(decision.input, null).ToString(),
        //        "time" => DateTime.Now.ToString(),
        //        "date" => DateTime.Now.Date.ToString(),
        //        _ => "Unknown tool"
        //    };
        //}
        //private async Task<string> GenerateFinalAnswer(List<ChatMessage> _chatMessages, HttpResponse httpResponse, CancellationToken cancellationToken)
        //{
        //    string finalResponse = "";

        //    await foreach (var item in _chatClient.GetStreamingResponseAsync(
        //            _chatMessages, cancellationToken: cancellationToken))
        //    {
        //        if (cancellationToken.IsCancellationRequested)
        //            break;

        //        var text = item.Text ?? string.Empty;

        //        finalResponse += text;

        //        await httpResponse.WriteAsync(text);
        //        await httpResponse.Body.FlushAsync();
        //    }
        //    return finalResponse;
        //}
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
