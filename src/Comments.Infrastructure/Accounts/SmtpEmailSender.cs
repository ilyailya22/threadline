using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Threadline.Comments.Application.Common.Abstractions;

namespace Threadline.Comments.Infrastructure.Accounts;

/// <summary>Sends mail over SMTP — Mailpit in development, a real relay anywhere else.</summary>
public sealed partial class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var mail = new MimeMessage
        {
            Subject = message.Subject,
            Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody(),
        };

        mail.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mail.To.Add(MailboxAddress.Parse(message.To));

        using var client = new SmtpClient();

        // StartTls where the relay offers it; a development Mailpit speaks plain SMTP and would
        // fail the handshake.
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mail, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        LogSent(logger, message.To, message.Subject);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent {Subject} to {Recipient}")]
    private static partial void LogSent(ILogger logger, string recipient, string subject);
}
