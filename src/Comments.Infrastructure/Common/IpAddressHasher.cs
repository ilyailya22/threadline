using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

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

/// <summary>
/// Turns a client IP into a stable, non-reversible identifier.
/// </summary>
/// <remarks>
/// A plain SHA-256 of an IP address is <em>not</em> anonymous: the whole IPv4 space is 4 billion
/// values, which a laptop brute-forces in seconds. Keyed HMAC with a secret pepper is what actually
/// makes the stored value useless to anyone who only has the database.
/// </remarks>
public interface IIpAddressHasher
{
    string Hash(string? ipAddress);
}

public sealed class IpAddressHasher : IIpAddressHasher, IDisposable
{
    private readonly HMACSHA256 _hmac;

    public IpAddressHasher(IOptions<PrivacyOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var pepper = options.Value.IpHashPepper;

        if (string.IsNullOrWhiteSpace(pepper))
        {
            throw new InvalidOperationException(
                $"{PrivacyOptions.SectionName}:{nameof(PrivacyOptions.IpHashPepper)} is not configured.");
        }

        _hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
    }

    public string Hash(string? ipAddress)
    {
        var value = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress.Trim();

        lock (_hmac)
        {
            return Convert.ToHexStringLower(_hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
    }

    public void Dispose() => _hmac.Dispose();
}
