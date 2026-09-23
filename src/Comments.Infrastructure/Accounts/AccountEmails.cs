using System.Net;
using Microsoft.Extensions.Options;
using Threadline.Comments.Application.Accounts;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Infrastructure.Accounts;

/// <summary>Writes the messages an account causes, and hands them to whatever sends mail.</summary>
public sealed class AccountEmails(IEmailSender sender, IOptions<EmailOptions> options) : IAccountEmails
{
    private readonly EmailOptions _options = options.Value;

    public Task SendConfirmationAsync(
        User account,
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        // The link is built from the configured public address, never from the incoming request:
        // a forged Host header would otherwise send the confirmation link to the forger's site.
        var link = $"{_options.PublicUrl.TrimEnd('/')}/confirm-email"
            + $"?id={account.Id}&token={Uri.EscapeDataString(token)}";

        var name = WebUtility.HtmlEncode(account.UserName.Value);

        var html = $"""
            <p>Hello {name},</p>
            <p>Confirm your e-mail address to finish setting up your Threadline account:</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Confirm my e-mail</a></p>
            <p>The link is good for {User.ConfirmationWindow.TotalHours:0} hours. If you did not create
            an account, ignore this message and nothing will happen.</p>
            """;

        var text = $"""
            Hello {account.UserName.Value},

            Confirm your e-mail address to finish setting up your Threadline account:
            {link}

            The link is good for {User.ConfirmationWindow.TotalHours:0} hours. If you did not create
            an account, ignore this message and nothing will happen.
            """;

        return sender.SendAsync(
            new EmailMessage(account.Email.Value, "Confirm your e-mail", html, text),
            cancellationToken);
    }
}
