using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Threadline.Comments.Infrastructure.Common;

/// <inheritdoc cref="IIpAddressHasher"/>
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
