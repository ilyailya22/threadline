using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts;

/// <summary>One place that turns a user into the shape the client reads.</summary>
public static class AccountMapper
{
    /// <summary>Where an uploaded avatar is served from. Built from the id, never from a file name.</summary>
    public static string AvatarUrlFor(Guid userId) => $"/api/accounts/{userId}/avatar";

    public static AccountDto ToDto(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new AccountDto(
            user.Id,
            user.UserName.Value,
            user.Email.Value,
            user.HomePage?.Value,
            user.AvatarPath is not null ? AvatarUrlFor(user.Id) : user.ExternalAvatarUrl,
            user.IsEmailConfirmed,
            user.PasswordHash is not null,
            user.GoogleSubject is not null);
    }
}
