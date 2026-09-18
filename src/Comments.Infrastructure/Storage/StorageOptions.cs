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
