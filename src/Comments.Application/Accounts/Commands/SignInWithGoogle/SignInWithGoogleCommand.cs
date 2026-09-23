using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;

namespace Threadline.Comments.Application.Accounts.Commands.SignInWithGoogle;

/// <summary>
/// What Google told us about the person who just came back from it.
/// </summary>
/// <param name="Subject">Google's stable id for them — the thing to match on, not the address.</param>
/// <param name="Email">Confirmed by Google, which is why these accounts skip our own confirmation.</param>
/// <param name="PictureUrl">Profile picture, used until they upload one of their own.</param>
public sealed record SignInWithGoogleCommand(
    string Subject,
    string Email,
    string? PictureUrl) : IRequest<AccountDto>;
