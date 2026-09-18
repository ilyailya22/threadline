namespace Threadline.Comments.Infrastructure.Common;

public sealed class PrivacyOptions
{
    public const string SectionName = "Privacy";

    /// <summary>
    /// Server-side pepper for IP hashing. Supplied from Key Vault in Azure and from user-secrets
    /// locally; the application refuses to start if it is left at its placeholder in production.
    /// </summary>
    public string IpHashPepper { get; set; } = string.Empty;
}
