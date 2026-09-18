using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Security;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Application.Captcha;

/// <inheritdoc cref="ICaptchaService"/>
public sealed partial class CaptchaService(
    ICaptchaCodeGenerator generator,
    ICaptchaImageRenderer renderer,
    ICaptchaStore store,
    IDateTimeProvider clock,
    ILogger<CaptchaService> logger) : ICaptchaService
{
    /// <summary>
    /// Long enough to type a form comfortably, short enough that a harvested challenge is worthless.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<CaptchaChallenge> IssueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var code = generator.Generate();
        var id = Guid.CreateVersion7(now);

        await store.StoreAsync(id, code, Lifetime, cancellationToken);

        return new CaptchaChallenge(id, renderer.Render(code), now.Add(Lifetime));
    }

    public async Task<bool> ValidateAsync(
        Guid challengeId,
        string? answer,
        CancellationToken cancellationToken = default)
    {
        if (challengeId == Guid.Empty)
        {
            return false;
        }

        // Consuming before looking at the answer is what makes this one-shot: a wrong or empty
        // answer burns the challenge too, so one image cannot be brute-forced with repeated guesses.
        var expected = await store.ConsumeAsync(challengeId, cancellationToken);

        if (expected is null)
        {
            LogUnknownChallenge(logger, challengeId);
            return false;
        }

        return !string.IsNullOrWhiteSpace(answer)
            && ConstantTime.AreEqual(expected.ToUpperInvariant(), answer.Trim().ToUpperInvariant());
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "CAPTCHA challenge {ChallengeId} was unknown, expired or already used")]
    private static partial void LogUnknownChallenge(ILogger logger, Guid challengeId);
}
