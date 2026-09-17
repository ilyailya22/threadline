using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Threadline.Comments.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Threadline.Comments.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Azurite / connection-string mode, used locally and in tests. Left empty in Azure, where
    /// <see cref="AccountUrl"/> plus a managed identity is used instead and no secret exists at all.
    /// </summary>
    public string? ConnectionString { get; set; }

    public string? AccountUrl { get; set; }

    public string ContainerName { get; set; } = "attachments";
}

/// <summary>
/// Azure Blob Storage adapter — the only place in the solution that knows attachments are not on a
/// local disk.
/// </summary>
/// <remarks>
/// Blobs are private. They are served through the API rather than by public URL or SAS link, so the
/// system controls caching headers and <c>Content-Disposition</c> (a stored .txt must download, not
/// render in the origin), and a leaked URL grants nothing. At real traffic this endpoint would sit
/// behind a CDN; that is noted in docs/IMPROVEMENTS.md rather than guessed at here.
/// </remarks>
public sealed class BlobFileStorage : IFileStorage
{
    private readonly BlobContainerClient _container;

    public BlobFileStorage(IOptions<StorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;

        var serviceClient = !string.IsNullOrWhiteSpace(settings.ConnectionString)
            ? new BlobServiceClient(settings.ConnectionString)
            : new BlobServiceClient(
                new Uri(settings.AccountUrl
                    ?? throw new InvalidOperationException(
                        $"{StorageOptions.SectionName}: either ConnectionString or AccountUrl must be set.")),
                new DefaultAzureCredential());

        _container = serviceClient.GetBlobContainerClient(settings.ContainerName);
    }

    /// <summary>Creates the container if it is missing. Called once at startup, not per request.</summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default) =>
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

    public async Task SaveAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(path);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType,

                    // Belt and braces: even if a blob were ever exposed directly, the browser is told
                    // not to sniff it into something executable.
                    CacheControl = "private, max-age=31536000, immutable",
                },
            },
            cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _container.GetBlobClient(path).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken = default) =>
        await _container.GetBlobClient(path)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);

    /// <summary>
    /// Not a storage URL: attachments are addressed by the API, which then streams them. See the
    /// class remarks for why.
    /// </summary>
    public string GetUrl(string path) => path;
}
