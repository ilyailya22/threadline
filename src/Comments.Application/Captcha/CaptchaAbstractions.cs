namespace Threadline.Comments.Application.Captcha;

/// <summary>A challenge handed to the browser: an opaque id plus the PNG the user has to read.</summary>
public sealed record CaptchaChallenge(Guid Id, byte[] ImagePng, DateTimeOffset ExpiresAt);

/// <summary>
/// The CAPTCHA required by the assignment.
/// </summary>
/// <remarks>
/// <para>
/// The answer never leaves the server: the client gets an id and an image, and the id is the only
/// thing it can send back. Answers live in Redis with a TTL, which also makes the whole mechanism
/// work unchanged across N stateless API replicas — a server-side session would not.
/// </para>
/// <para>
/// Validation is <em>one-shot</em>: a successful check deletes the entry atomically, so a captured
/// (id, answer) pair cannot be replayed to post a thousand comments.
/// </para>
/// </remarks>
public interface ICaptchaService
{
    Task<CaptchaChallenge> IssueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes the challenge. Returns <see langword="false"/> for a wrong answer, an unknown id,
    /// an expired id, or an id that has already been used.
    /// </summary>
    Task<bool> ValidateAsync(Guid challengeId, string? answer, CancellationToken cancellationToken = default);
}

/// <summary>Redis-backed storage of pending challenges.</summary>
public interface ICaptchaStore
{
    Task StoreAsync(Guid challengeId, string answer, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Atomically reads and deletes the stored answer.</summary>
    Task<string?> ConsumeAsync(Guid challengeId, CancellationToken cancellationToken = default);
}

/// <summary>Renders the challenge text into a distorted PNG.</summary>
public interface ICaptchaImageRenderer
{
    byte[] Render(string code);
}

/// <summary>Generates the challenge text: latin letters and digits, as the assignment requires.</summary>
public interface ICaptchaCodeGenerator
{
    string Generate();
}
