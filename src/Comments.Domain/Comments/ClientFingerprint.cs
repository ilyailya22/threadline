using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Comments;

/// <summary>
/// Request-scoped data that helps identify the client behind a comment, as the assignment requires.
/// </summary>
/// <remarks>
/// The IP address is stored <em>hashed</em> (HMAC-SHA256 with a server-side pepper held in Key
/// Vault), never in clear text. That still lets us group posts by origin, rate-limit and investigate
/// abuse, but a database leak does not hand out the browsing history of every visitor. This is a
/// deliberate trade-off in favour of data minimisation.
/// </remarks>
public sealed class ClientFingerprint : ValueObject
{
    public const int IpHashLength = 64; // hex-encoded SHA-256
    public const int MaxUserAgentLength = 512;

    private ClientFingerprint(string ipHash, string? userAgent, Guid? clientId)
    {
        IpHash = ipHash;
        UserAgent = userAgent;
        ClientId = clientId;
    }

    /// <summary>Hex-encoded HMAC-SHA256 of the remote IP address.</summary>
    public string IpHash { get; }

    public string? UserAgent { get; }

    /// <summary>Opaque id from a first-party cookie, stable across a browser's visits.</summary>
    public Guid? ClientId { get; }

    public static ClientFingerprint Create(string ipHash, string? userAgent, Guid? clientId)
    {
        if (string.IsNullOrWhiteSpace(ipHash) || ipHash.Length != IpHashLength)
        {
            throw new DomainException("Client IP hash is missing or malformed.");
        }

        var trimmedAgent = userAgent is null
            ? null
            : userAgent[..Math.Min(userAgent.Length, MaxUserAgentLength)];

        return new ClientFingerprint(ipHash, trimmedAgent, clientId);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return IpHash;
        yield return UserAgent;
        yield return ClientId;
    }
}
