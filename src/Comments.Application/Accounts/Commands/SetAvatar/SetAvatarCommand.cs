using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Attachments;

namespace Threadline.Comments.Application.Accounts.Commands.SetAvatar;

/// <summary>Replaces the account's picture with an uploaded one.</summary>
public sealed record SetAvatarCommand(AttachmentUpload Upload) : IRequest<AccountDto>;
