using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Comments.Dtos;
using MediatR;

namespace Threadline.Comments.Application.Comments.Commands.CreateComment;

/// <summary>
/// Posts a comment — either a new top-level entry or a reply to any existing comment.
/// </summary>
/// <param name="UserName">Latin letters and digits, required.</param>
/// <param name="Email">Required, e-mail format.</param>
/// <param name="HomePage">Optional URL.</param>
/// <param name="Text">Raw message text; sanitised by the handler.</param>
/// <param name="ParentId">The comment being replied to, or <see langword="null"/> for a new thread.</param>
/// <param name="CaptchaId">Id of the challenge the user was shown.</param>
/// <param name="CaptchaAnswer">What the user typed.</param>
/// <param name="Attachment">Optional image or text file.</param>
public sealed record CreateCommentCommand(
    string UserName,
    string Email,
    string? HomePage,
    string Text,
    Guid? ParentId,
    Guid CaptchaId,
    string CaptchaAnswer,
    AttachmentUpload? Attachment) : IRequest<CreateCommentResultDto>;
