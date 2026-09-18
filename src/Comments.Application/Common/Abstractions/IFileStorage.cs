namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>Object storage for attachments — Azure Blob Storage in production, Azurite locally.</summary>
public interface IFileStorage
{
    Task SaveAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Opens a stored file, or returns <see langword="null"/> if there is none at that path.</summary>
    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
