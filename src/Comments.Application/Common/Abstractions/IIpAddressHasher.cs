namespace Threadline.Comments.Application.Common.Abstractions;

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
