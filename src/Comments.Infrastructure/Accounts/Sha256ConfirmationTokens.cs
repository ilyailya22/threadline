using System.Security.Cryptography;
using Threadline.Comments.Application.Common.Abstractions;

namespace Threadline.Comments.Infrastructure.Accounts;

/// <summary>
/// Confirmation tokens: 256 random bits in the link, their SHA-256 in the database.
/// </summary>
/// <remarks>
/// A plain hash rather than a slow one, and that is the right call here: unlike a password, the
/// token is already full-entropy random, so there is nothing to guess and nothing for a work
/// factor to slow down. Storing the hash still means a leaked database hands out no working links.
/// </remarks>
public sealed class Sha256ConfirmationTokens : IConfirmationTokens
{
    public ConfirmationToken Issue()
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));

        return new ConfirmationToken(token, HashOf(token));
    }

    public string HashOf(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>URL-safe, so the token survives being put in a link without escaping.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
