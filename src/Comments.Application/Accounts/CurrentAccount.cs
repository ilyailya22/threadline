using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts;

/// <summary>
/// Loads the signed-in account, or refuses. Every settings use case starts this way, and the
/// alternative was the same six lines in each of them.
/// </summary>
internal static class CurrentAccount
{
    public static async Task<User> LoadAsync(
        ICurrentUser currentUser,
        IUserRepository users,
        CancellationToken cancellationToken)
    {
        var id = currentUser.Id ?? throw new NotFoundException("Account", Guid.Empty);

        return await users.FindByIdAsync(id, cancellationToken) is { IsRegistered: true } account
            ? account
            : throw new NotFoundException("Account", id);
    }
}
