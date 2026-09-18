namespace Threadline.Comments.Application.Captcha;

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
/// Validation is <em>one-shot</em>: a check deletes the entry atomically, so a captured
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
