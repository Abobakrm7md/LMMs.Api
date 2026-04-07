using LMMs.Api.Interfaces;
using Microsoft.Extensions.AI;
using System.Text.Json;
using LMMs.Api.ViewModels;

namespace LMMs.Api.Services
{
    public class LLMSDecision : ILLMSDecision
    {
        private readonly IChatClient _chatClient;

        public LLMSDecision(IChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        public async Task<AgentDecision> GetDecision(string context)
        {
            var prompt = $@"
                    You are an AI Agent.

                    Your job is ONLY to decide the next step.

                    STRICT RULES:
                    - DO NOT answer the user
                    - DO NOT explain anything
                    - ONLY return valid JSON
                    - NO markdown
                    - NO extra text

                    AVAILABLE TOOLS:
                    - calculator → for math calculations
                    - time → for date, time, day

                    RULES:
                    - Use tools ONLY if needed
                    - For date/time/day → use tool ""time""
                    - For math → use tool ""calculator""
                    - For greetings → use action ""answer""

                   IMPORTANT:
                    - You are allowed to call ONLY ONE tool per response
                    - NEVER include multiple tools in the same response
                    - If the user needs multiple steps → return only the FIRST step

                    STRICT FORMAT:
                    {{
                      ""action"": ""tool"",
                      ""tool"": ""<tool_name>"",
                      ""input"": ""<input>""
                    }}

                    OR

                    {{
                      ""action"": ""answer"",
                      ""text"": ""<final answer>""
                    }}

                    Examples:

                    User: hello
                    {{ ""action"": ""answer"" }}

                    User: what is 5 * 10?
                    {{ ""action"": ""tool"", ""tool"": ""calculator"", ""input"": ""5 * 10"" }}

                    User: what time is it?
                    {{ ""action"": ""tool"", ""tool"": ""time"", ""input"": """" }}

                    User: what day is today?
                    {{ ""action"": ""tool"", ""tool"": ""time"", ""input"": ""day"" }}

                    ---

                    Context:
                    {context}

                    Now respond with JSON only:
";

            var response = await _chatClient.GetResponseAsync(prompt);
            var content = response.Text?.Trim();

            return ParseDecision(content);
        }
        private AgentDecision ParseDecision(string content)
        {
            try
            {
                var decision = JsonSerializer.Deserialize<AgentDecision>(content,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                // 🔥 Normalize tool names
                decision.tool = NormalizeTool(decision.tool);

                // 🔥 Guardrails
                if (decision.action != "tool" && decision.action != "answer")
                {
                    decision.action = "answer";
                }

                if (decision.action == "tool" &&
                    decision.tool != "calculator" &&
                    decision.tool != "time")
                {
                    decision.action = "answer";
                }

                return decision;
            }
            catch
            {
                // 🔥 fallback
                return new AgentDecision
                {
                    action = "answer"
                };
            }
        }
        private string NormalizeTool(string tool)
        {
            return tool?.ToLower() switch
            {
                "date" => "time",
                "day" => "time",
                "datetime" => "time",
                "now" => "time",
                _ => tool
            };
        }
    }
}
