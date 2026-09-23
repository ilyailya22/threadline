namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>Sends the few transactional messages this application has to send.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

/// <param name="To">The recipient's address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="Html">The message as HTML.</param>
/// <param name="Text">The same message as plain text, for clients that prefer it.</param>
public sealed record EmailMessage(string To, string Subject, string Html, string Text);
