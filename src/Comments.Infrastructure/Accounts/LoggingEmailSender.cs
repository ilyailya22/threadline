using Microsoft.Extensions.Logging;
using Threadline.Comments.Application.Common.Abstractions;

namespace Threadline.Comments.Infrastructure.Accounts;

/// <summary>
/// Writes the message to the log instead of sending it, for when no SMTP host is configured.
/// </summary>
/// <remarks>
/// The alternative — throwing — would make registration fail on a machine that has everything
/// else working, and the alternative to that — swallowing — would leave someone waiting for an
/// e-mail that was never going anywhere. In the log, the confirmation link is at least findable.
/// </remarks>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogNotSent(logger, message.To, message.Subject, message.Text);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No SMTP host configured; {Subject} for {Recipient} was not sent. Body: {Body}")]
    private static partial void LogNotSent(ILogger logger, string recipient, string subject, string body);
}
