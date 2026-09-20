using System.ComponentModel;
using System.Reflection;
using LMMs.Api.Interfaces;
using LMMs.Api.ViewModels;

namespace LMMs.Api.Planning
{
    public static class AgentToolSelector
    {
        public static IReadOnlyList<IAgentTool> Select(IEnumerable<IAgentTool> tools, ChatRequest request)
        {
            if (!request.EnableTools && request.File is null)
                return [];

            IEnumerable<IAgentTool> selected = tools;

            if (!request.EnableTools && request.File is not null)
                selected = selected.Where(IsFileReaderTool);
            else if (request.EnableTools && !request.EnableWebSearch)
                selected = selected.Where(t => !IsWebSearchTool(t));
            else if (request.EnableTools && request.File is null)
                selected = selected.Where(t => !IsFileReaderTool(t));

            return selected.ToList();
        }

        public static IReadOnlyList<ToolDescriptor> Describe(IEnumerable<IAgentTool> tools) =>
            tools.Select(Describe).ToList();

        public static ToolDescriptor Describe(IAgentTool tool)
        {
            var method = tool.GetFunction().Method;
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description
                              ?? tool.Name;
            var parameters = method.GetParameters()
                .Where(p => p.ParameterType != typeof(CancellationToken))
                .Select(p => p.Name ?? "arg")
                .ToList();

            return new ToolDescriptor
            {
                Name = tool.Name,
                Description = description,
                Parameters = parameters
            };
        }

        private static bool IsWebSearchTool(IAgentTool t) =>
            string.Equals(t.Name, "web_search", StringComparison.OrdinalIgnoreCase);

        private static bool IsFileReaderTool(IAgentTool t) =>
            string.Equals(t.Name, "read_file", StringComparison.OrdinalIgnoreCase);
    }
}
