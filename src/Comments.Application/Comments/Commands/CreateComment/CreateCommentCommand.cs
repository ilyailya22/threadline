using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Comments.Dtos;
using MediatR;

namespace Threadline.Comments.Application.Comments.Commands.CreateComment;

/// <summary>
/// Posts a comment — either a new top-level entry or a reply to any existing comment.
/// </summary>
/// <remarks>
/// Two ways in. A signed-in account brings its own identity, so the name, the address and the
/// CAPTCHA are neither asked for nor read. A guest types a name and an address and solves a
/// CAPTCHA, exactly as the assignment describes.
/// </remarks>
/// <param name="AuthorId">The signed-in account, set by the API from the cookie; never from the request body.</param>
/// <param name="UserName">Latin letters and digits. Required for a guest, ignored for an account.</param>
/// <param name="Email">E-mail format. Required for a guest, ignored for an account.</param>
/// <param name="HomePage">Optional URL. Guests only; an account keeps its own in settings.</param>
/// <param name="Text">Raw message text; sanitised by the handler.</param>
/// <param name="ParentId">The comment being replied to, or <see langword="null"/> for a new thread.</param>
/// <param name="CaptchaId">Id of the challenge the user was shown.</param>
/// <param name="CaptchaAnswer">What the user typed.</param>
/// <param name="Attachment">Optional image or text file.</param>
public sealed record CreateCommentCommand(
    Guid? AuthorId,
    string UserName,
    string Email,
    string? HomePage,
    string Text,
    Guid? ParentId,
    Guid CaptchaId,
    string CaptchaAnswer,
    AttachmentUpload? Attachment) : IRequest<CreateCommentResultDto>;
