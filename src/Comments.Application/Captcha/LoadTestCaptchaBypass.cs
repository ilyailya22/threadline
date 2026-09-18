using Threadline.Comments.Application.Common.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Threadline.Comments.Application.Captcha;

/// <summary>
/// Lets a load test post comments without solving a CAPTCHA.
/// </summary>
/// <remarks>
/// <para>
/// A CAPTCHA that a script can solve is not a CAPTCHA, so a write benchmark has exactly two honest
/// options: measure the 400 that every submission gets, or provide a deliberate, visible, tightly
/// scoped bypass. This is the second one.
/// </para>
/// <para>
/// Three things keep it from becoming a back door. It is a decorator, so removing one registration
/// removes the behaviour entirely and the real service is untouched. It requires both an explicit
/// <c>Enabled = true</c> and a non-empty secret, so a partial configuration fails closed. And the
/// composition root refuses to register it at all when the environment is Production — see
/// <c>Program.cs</c> — so it cannot be switched on by an environment variable on a live box.
/// </para>
/// </remarks>
public sealed partial class LoadTestCaptchaBypass(
    ICaptchaService inner,
    IOptions<LoadTestOptions> options,
    ILogger<LoadTestCaptchaBypass> logger) : ICaptchaService
{
    private readonly LoadTestOptions _options = options.Value;

    public Task<CaptchaChallenge> IssueAsync(CancellationToken cancellationToken = default) =>
        inner.IssueAsync(cancellationToken);

    public async Task<bool> ValidateAsync(
        Guid challengeId,
        string? answer,
        CancellationToken cancellationToken = default)
    {
        if (_options.Enabled
            && !string.IsNullOrWhiteSpace(_options.BypassAnswer)
            && !string.IsNullOrWhiteSpace(answer)
            && ConstantTime.AreEqual(_options.BypassAnswer, answer))
        {
            LogBypassed(logger);

            // Still consume the challenge, so the bypass does not also change the storage behaviour
            // being measured — a load test should exercise the same Redis round trip a user causes.
            await inner.ValidateAsync(challengeId, answer: null, cancellationToken);

            return true;
        }

        return await inner.ValidateAsync(challengeId, answer, cancellationToken);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "CAPTCHA bypassed by the load-test token. This must never appear in production logs.")]
    private static partial void LogBypassed(ILogger logger);
}
