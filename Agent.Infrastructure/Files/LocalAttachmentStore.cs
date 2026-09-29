using Agent.Application.Files;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace Agent.Infrastructure.Files;

public sealed class AttachmentStorageOptions
{
    public string Directory { get; set; } = "Files";
    public int MaximumCharacters { get; set; } = 4000;
}

public sealed class LocalAttachmentStore(AttachmentStorageOptions options) : IAttachmentStore
{
    public async Task<StoredAttachment> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        var original = Path.GetFileName(fileName);
        var id = $"{Guid.NewGuid():N}{Path.GetExtension(original).ToLowerInvariant()}";
        System.IO.Directory.CreateDirectory(options.Directory);
        await using var output = File.Create(Path.Combine(options.Directory, id));
        await content.CopyToAsync(output, cancellationToken);
        return new StoredAttachment(id, original);
    }

    public async Task<string> ReadTextAsync(string attachmentId, CancellationToken cancellationToken)
    {
        var safeId = Path.GetFileName(attachmentId);
        if (!string.Equals(safeId, attachmentId, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid attachment identifier.");
        var path = Path.Combine(options.Directory, safeId);
        if (!File.Exists(path)) return $"File not found: {safeId}";
        var text = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".txt" or ".json" or ".xml" or ".md" => await File.ReadAllTextAsync(path, cancellationToken),
            ".csv" => string.Join('\n', (await File.ReadAllLinesAsync(path, cancellationToken)).Take(20)),
            ".pdf" => ReadPdf(path),
            ".docx" => ReadDocx(path),
            var extension => $"Unsupported file type: {extension}"
        };
        return text.Length <= options.MaximumCharacters ? text : text[..options.MaximumCharacters] + "\n... (truncated)";
    }

    private static string ReadPdf(string path)
    {
        using var document = PdfDocument.Open(path);
        return string.Join('\n', document.GetPages().Take(5).Select(page => page.Text));
    }

    private static string ReadDocx(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        return document.MainDocumentPart?.Document.Body?.InnerText ?? "Could not read file.";
    }
}
