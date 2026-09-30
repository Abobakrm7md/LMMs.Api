using System.Runtime.CompilerServices;

namespace Agent.Application.Agents;

/// <summary>Specialized entry point for attachment requests; execution remains in the existing planning agent.</summary>
public sealed class FileAnalysisAgent(PlanningAgent planningAgent) : ISpecializedAgent
{
    public AgentDescriptor Descriptor { get; } = new(
        "FileAnalysisAgent",
        "Analyzes supported uploaded documents using the existing file-reading tool.",
        ["file-analysis", "attachments", "pdf", "docx", "text"]);

    public bool CanHandle(AgentTurnRequest request) =>
        !string.IsNullOrWhiteSpace(request.AttachmentId);

    public async IAsyncEnumerable<AgentTurnEvent> RunTurnAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in planningAgent.RunTurnAsync(request, cancellationToken)
                           .WithCancellation(cancellationToken))
            yield return item;
    }
}
