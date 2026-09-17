using LMMs.Api.Interfaces;
using LMMs.Api.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace LMMs.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IChatClient _chatClient;
        private readonly IAgentAnswer _agentAnswer;
        // Store chat messages for the entire session (simple demo)
        private static readonly List<ChatMessage> _chatMessages = new();

        public ChatController(IChatClient chatClient, IAgentAnswer agentAnswer)
        {
            _chatClient = chatClient;
            _agentAnswer = agentAnswer;
        }

        [HttpPost("/api/chat")]
        public async Task Stream([FromForm] ChatRequest request)
        {
            Response.ContentType = "text/event-stream";
            var cancellationToken = HttpContext.RequestAborted;

            await foreach (var piece in _agentAnswer.RunAgentUsingAIFunctions(request, _chatMessages, cancellationToken))
            {
                await Response.WriteAsync(piece);
                await Response.Body.FlushAsync();
               // Console.Write(piece);
            }

            #region Commented
            //var finalResponse = await  _agentAnswer.RunAgent(request.Prompt, _chatMessages, Response, cancellationToken);


            //// 🔥 Step 1: Get decision from LLM
            //var decision = await GetDecision(request);
            //string finalResponse = "";

            //// 🔥 Step 2: Tool execution OR LLM streaming
            //if (decision.action == "tool")
            //{
            //    finalResponse = ExecuteTool(decision);

            //    await Response.WriteAsync(finalResponse);
            //}
            //else
            //{
            //    // Stream normal response
            //    await foreach (var item in _chatClient.GetStreamingResponseAsync(
            //        _chatMessages, cancellationToken: cancellationToken))
            //    {
            //        if (cancellationToken.IsCancellationRequested)
            //            break;

            //        var text = item.Text ?? string.Empty;

            //        finalResponse += text;

            //        await Response.WriteAsync(text);
            //        await Response.Body.FlushAsync();
            //    }
            //}

            // 🔥 Save assistant response
            //if (!string.IsNullOrWhiteSpace(finalResponse))
            //{
            //    _chatMessages.Add(new ChatMessage(ChatRole.Assistant, finalResponse));
            //}
            #endregion
        
        }
        #region Commented
        //private async Task<AgentDecision> GetDecision(ChatRequest request)
        //{
        //    var prompt = $@"
        //                You are an AI Agent.

        //                Your job is ONLY to decide what to do next.

        //                STRICT RULES:
        //                - DO NOT answer the user
        //                - ONLY return JSON

        //                Examples:

        //                User: hello
        //                Response:
        //                {{ ""action"": ""answer"" }}

        //                User: what is 5 * 10?
        //                Response:
        //                {{ ""action"": ""tool"", ""tool"": ""calculator"", ""input"": ""5 * 10"" }}

        //                User: what time is it?
        //                Response:
        //                {{ ""action"": ""tool"", ""tool"": ""time"" }}

        //                Now decide:

        //                User: {request.Prompt}
        //                ";

        //    var response = await _chatClient.GetResponseAsync(prompt);
        //    var content = response.Text;

        //    return JsonSerializer.Deserialize<AgentDecision>(content);
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
        #endregion
    }
}
