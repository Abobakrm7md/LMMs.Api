using Agent.Application.Files;
using Agent.Application.Tools;
using Agent.Domain.Tools;
namespace Agent.Infrastructure.Tools.Files;
public sealed class FileReaderTool(IAttachmentStore attachments) : IAgentTool
{
    public ToolDefinition Definition { get; } = new("read_file", "Read the uploaded file when the answer requires its contents. Supports txt, csv, json, xml, md, pdf, and docx.", ["source"], ToolCategory.File);
    public async Task<ToolResult> ExecuteAsync(ToolContext context, CancellationToken cancellationToken)
    {
        if (context.AttachmentId is null) return ToolResult.Failure(0, "", Definition.Name, context.Arguments, "No file attached.", "No file attached.");
        var output = await attachments.ReadTextAsync(context.AttachmentId, cancellationToken);
        return output.StartsWith("File not found", StringComparison.OrdinalIgnoreCase)
            ? ToolResult.Failure(0, "", Definition.Name, context.Arguments, output, output)
            : ToolResult.Success(0, "", Definition.Name, context.Arguments, output);
    }
}
