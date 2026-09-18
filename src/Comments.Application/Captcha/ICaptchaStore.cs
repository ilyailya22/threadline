namespace Threadline.Comments.Application.Captcha;

/// <summary>Storage of pending challenges (Redis).</summary>
public interface ICaptchaStore
{
    Task StoreAsync(Guid challengeId, string answer, TimeSpan ttl, CancellationToken cancellationToken = default);

    /// <summary>Atomically reads and deletes the stored answer.</summary>
    Task<string?> ConsumeAsync(Guid challengeId, CancellationToken cancellationToken = default);
}
