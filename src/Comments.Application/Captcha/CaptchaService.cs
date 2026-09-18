using System.Security.Cryptography;
using Threadline.Comments.Application.Common.Abstractions;
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
        var code = generator.Generate();
        var id = Guid.CreateVersion7(clock.UtcNow);

        await store.StoreAsync(id, code, Lifetime, cancellationToken);

        return new CaptchaChallenge(id, renderer.Render(code), clock.UtcNow.Add(Lifetime));
    }

    public async Task<bool> ValidateAsync(
        Guid challengeId,
        string? answer,
        CancellationToken cancellationToken = default)
    {
        if (challengeId == Guid.Empty || string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        // Consuming before comparing is what makes this one-shot: a wrong answer burns the
        // challenge too, so an attacker cannot brute-force one image with repeated guesses.
        var expected = await store.ConsumeAsync(challengeId, cancellationToken);

        if (expected is null)
        {
            LogUnknownChallenge(logger, challengeId);
            return false;
        }

        return FixedTimeEquals(expected, answer.Trim());
    }

    /// <summary>
    /// Constant-time comparison. A CAPTCHA is not a secret worth a timing attack, but comparing
    /// user-supplied strings in constant time is a habit worth keeping uniform across a codebase —
    /// the day it is a session token, nobody has to remember to switch.
    /// </summary>
    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(expected.ToUpperInvariant());
        var b = System.Text.Encoding.UTF8.GetBytes(actual.ToUpperInvariant());

        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "CAPTCHA challenge {ChallengeId} was unknown, expired or already used")]
    private static partial void LogUnknownChallenge(ILogger logger, Guid challengeId);
}

/// <summary>
/// Generates the challenge text.
/// </summary>
/// <remarks>
/// Characters that are easy to confuse in a distorted image — 0/O, 1/I/l, 5/S, 2/Z — are left out.
/// Rejecting a human who read the image correctly is a worse failure than a slightly smaller
/// alphabet: 32^5 is still 33 million combinations against a one-shot, rate-limited challenge.
/// </remarks>
public sealed class CaptchaCodeGenerator : ICaptchaCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRTUVWXY346789";
    private const int Length = 5;

    public string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}
