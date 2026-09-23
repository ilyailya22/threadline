using Azure.Identity;
using Azure.Storage.Blobs;
using Threadline.Comments.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;

namespace Threadline.Comments.Api.Extensions;

/// <summary>
/// Where the keys that encrypt the auth cookie live.
/// </summary>
/// <remarks>
/// By default ASP.NET Core generates a key ring in memory, which is correct for one process and
/// wrong for several: the API runs two to ten replicas, so a cookie issued by one of them could not
/// be read by any of the others and a session ended on the request after it began. Nothing failed
/// loudly — the cookie simply did not decrypt, and the caller looked like a guest again.
/// <para>
/// The ring is therefore kept in the storage account the deployment already has, reached with the
/// same managed identity as the attachments, so no new secret exists. Locally, and anywhere without
/// a storage account configured, the in-memory default is left alone: a single instance has nothing
/// to share the keys with, and writing them to disk would only leave a file behind.
/// </para>
/// </remarks>
public static class DataProtectionExtensions
{
    /// <summary>Container for the key ring. Separate from attachments, which are served to the public.</summary>
    private const string ContainerName = "data-protection";

    private const string BlobName = "keys.xml";

    public static IServiceCollection AddSharedDataProtection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>();

        var client = ContainerFor(storage);

        if (client is null)
        {
            return services;
        }

        // Created if missing: the keys have to be writable on first boot, and a deployment that
        // came up before the container existed would otherwise fail in a way nobody sees until a
        // second replica appears.
        client.CreateIfNotExists();

        services
            .AddDataProtection()
            .SetApplicationName("threadline-comments")
            .PersistKeysToAzureBlobStorage(client.GetBlobClient(BlobName));

        return services;
    }

    private static BlobContainerClient? ContainerFor(StorageOptions? storage)
    {
        if (storage is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(storage.ConnectionString))
        {
            return new BlobServiceClient(storage.ConnectionString).GetBlobContainerClient(ContainerName);
        }

        return string.IsNullOrWhiteSpace(storage.AccountUrl)
            ? null
            : new BlobServiceClient(new Uri(storage.AccountUrl), new DefaultAzureCredential())
                .GetBlobContainerClient(ContainerName);
    }
}
