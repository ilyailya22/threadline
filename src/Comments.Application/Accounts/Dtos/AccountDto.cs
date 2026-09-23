namespace Threadline.Comments.Application.Accounts.Dtos;

/// <summary>The signed-in person, as the client needs to render them.</summary>
/// <param name="Id">Account id; also the author id on the comments they write.</param>
/// <param name="UserName">The nickname shown on comments — derived from the address at sign-up, changeable since.</param>
/// <param name="Email">The address the account owns.</param>
/// <param name="HomePage">Optional, shown as a link on their comments.</param>
/// <param name="AvatarUrl">Uploaded picture, Google's picture, or nothing — then the UI draws initials.</param>
/// <param name="IsEmailConfirmed">False until the confirmation link is followed; Google accounts start confirmed.</param>
/// <param name="HasPassword">False for an account that only ever signed in with Google.</param>
/// <param name="IsGoogleLinked">True when Google can sign this account in.</param>
public sealed record AccountDto(
    Guid Id,
    string UserName,
    string Email,
    string? HomePage,
    string? AvatarUrl,
    bool IsEmailConfirmed,
    bool HasPassword,
    bool IsGoogleLinked);
