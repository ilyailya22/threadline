using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts;

/// <summary>
/// The messages an account causes. Composing them — subject, wording, the link's shape — needs the
/// public address of the site, which is configuration, so it lives in infrastructure; the use case
/// only says what happened.
/// </summary>
public interface IAccountEmails
{
    Task SendConfirmationAsync(User account, string token, CancellationToken cancellationToken = default);
}
