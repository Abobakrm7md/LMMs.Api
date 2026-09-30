namespace Agent.Application.Files;

public sealed record StoredAttachment(string Id, string OriginalName);
public interface IAttachmentStore
{
    Task<StoredAttachment> SaveAsync(string fileName, Stream content, CancellationToken cancellationToken);
    Task<string> ReadTextAsync(string attachmentId, CancellationToken cancellationToken);
}
