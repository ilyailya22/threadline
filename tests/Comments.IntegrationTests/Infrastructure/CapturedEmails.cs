using System.Collections.Concurrent;
using Threadline.Comments.Application.Accounts;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for the thing that sends e-mail, and remembers what it was asked to send.
/// </summary>
/// <remarks>
/// A confirmation token exists in exactly two places: the link in the message, and a hash in the
/// database. A test that wants to follow the link has to read the message, so this is the
/// mailbox.
/// </remarks>
public sealed class CapturedEmails : IAccountEmails
{
    private readonly ConcurrentDictionary<string, string> _tokensByEmail = new(StringComparer.OrdinalIgnoreCase);

    public Task SendConfirmationAsync(User account, string token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        _tokensByEmail[account.Email.Value] = token;

        return Task.CompletedTask;
    }

    /// <summary>The token last sent to an address, or null if nothing was.</summary>
    public string? TokenFor(string email) =>
        _tokensByEmail.TryGetValue(email, out var token) ? token : null;
}
