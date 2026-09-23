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
/// <para>
/// Nothing identifying the author is marked required, because whether it is required depends on
/// the cookie, which this model cannot see: a guest must supply a name, an address and a CAPTCHA,
/// an account must not supply any of them. Only the validator knows which case it is, so these
/// stay optional here and are enforced there. What remains are ceilings, and a ceiling holds
/// either way.
/// </para>
/// </remarks>
public sealed class CreateCommentRequest
{
    [StringLength(Domain.Users.UserName.MaxLength)]
    public string? UserName { get; set; }

    [StringLength(EmailAddress.MaxLength)]
    public string? Email { get; set; }

    [StringLength(HomePageUrl.MaxLength)]
    public string? HomePage { get; set; }

    [Required]
    [StringLength(CommentBody.MaxHtmlLength, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;

    /// <summary>The comment being replied to; omit to start a new thread.</summary>
    public Guid? ParentId { get; set; }

    public Guid? CaptchaId { get; set; }

    [StringLength(CaptchaAnswerFormat.MaxLength)]
    public string? CaptchaAnswer { get; set; }

    /// <summary>Optional image (JPG/GIF/PNG) or text file (TXT).</summary>
    public IFormFile? File { get; set; }

    /// <summary>
    /// The command this form becomes, with the file (if any) already opened by the caller and the
    /// author taken from the cookie rather than from the form.
    /// </summary>
    public CreateCommentCommand ToCommand(Guid? authorId, AttachmentUpload? attachment) =>
        new(
            authorId,
            UserName ?? string.Empty,
            Email ?? string.Empty,
            HomePage,
            Text,
            ParentId,
            CaptchaId ?? Guid.Empty,
            CaptchaAnswer ?? string.Empty,
            attachment);
}
