using System.ComponentModel.DataAnnotations;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Comments.Commands.CreateComment;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Api.Contracts;

/// <summary>
/// The comment form, as posted.
/// </summary>
/// <remarks>
/// The data annotations here are a cheap first gate that rejects obvious junk before it reaches the
/// pipeline. They are not the authority: the real rules live in
/// <c>CreateCommentCommandValidator</c> and in the domain value objects, and those run on every
/// request regardless of what this model says.
/// </remarks>
public sealed class CreateCommentRequest
{
    [Required]
    [StringLength(Domain.Users.UserName.MaxLength, MinimumLength = Domain.Users.UserName.MinLength)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [StringLength(EmailAddress.MaxLength)]
    public string Email { get; set; } = string.Empty;

    [StringLength(HomePageUrl.MaxLength)]
    public string? HomePage { get; set; }

    [Required]
    [StringLength(CommentBody.MaxHtmlLength, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;

    /// <summary>The comment being replied to; omit to start a new thread.</summary>
    public Guid? ParentId { get; set; }

    [Required]
    public Guid CaptchaId { get; set; }

    [Required]
    [StringLength(CaptchaAnswerFormat.MaxLength, MinimumLength = 1)]
    public string CaptchaAnswer { get; set; } = string.Empty;

    /// <summary>Optional image (JPG/GIF/PNG) or text file (TXT).</summary>
    public IFormFile? File { get; set; }

    /// <summary>
    /// The command this form becomes, with the file (if any) already opened by the caller and the
    /// author taken from the cookie rather than from the form.
    /// </summary>
    public CreateCommentCommand ToCommand(Guid? authorId, AttachmentUpload? attachment) =>
        new(authorId, UserName, Email, HomePage, Text, ParentId, CaptchaId, CaptchaAnswer, attachment);
}
