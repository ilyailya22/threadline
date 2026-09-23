using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;

namespace Threadline.Comments.Application.Accounts.Commands.RemoveAvatar;

/// <summary>Drops the uploaded picture, falling back to Google's or to initials.</summary>
public sealed record RemoveAvatarCommand : IRequest<AccountDto>;
