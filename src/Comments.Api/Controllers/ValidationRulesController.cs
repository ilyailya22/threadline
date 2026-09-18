using Threadline.Comments.Api.Contracts;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Comments.Sanitization;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Threadline.Comments.Api.Controllers;

/// <summary>
/// Publishes the validation rules the server enforces, so the client can enforce the same ones.
/// </summary>
/// <remarks>
/// The assignment asks for validation on both the client and the server. The usual way to do that
/// is to write each regex twice and watch them drift apart; here the patterns and limits have one
/// definition — the domain value objects — and the Angular form builds its validators from this
/// endpoint. The server never trusts the client's check; it just stops them disagreeing.
/// </remarks>
[ApiController]
[Route("api/validation-rules")]
[Produces("application/json")]
public sealed class ValidationRulesController : ControllerBase
{
    private static readonly SniffedFileType[] ImageTypes = TypesOf(AttachmentKind.Image);

    private static readonly SniffedFileType[] TextTypes = TypesOf(AttachmentKind.TextFile);

    [HttpGet]
    [OutputCache(Duration = 3600)]
    [ProducesResponseType<ValidationRulesResponse>(StatusCodes.Status200OK)]
    public ActionResult<ValidationRulesResponse> Get() =>
        Ok(new ValidationRulesResponse
        {
            UserName = new FieldRules
            {
                Required = true,
                Pattern = UserName.Pattern,
                MinLength = UserName.MinLength,
                MaxLength = UserName.MaxLength,
                Description = "Latin letters and digits only.",
            },
            Email = new FieldRules
            {
                Required = true,
                Pattern = EmailAddress.Pattern,
                MaxLength = EmailAddress.MaxLength,
                Description = "A valid e-mail address.",
            },
            HomePage = new FieldRules
            {
                Required = false,
                MaxLength = HomePageUrl.MaxLength,
                Description = "Absolute http or https URL.",
            },
            Text = new FieldRules
            {
                Required = true,
                MinLength = 1,
                MaxLength = CommentBody.MaxHtmlLength,
                Description = "Only the allowed tags survive; every tag must be closed.",
            },
            Captcha = new FieldRules
            {
                Required = true,
                Pattern = CaptchaAnswerFormat.Pattern,
                MaxLength = CaptchaAnswerFormat.MaxLength,
                Description = "Latin letters and digits only.",
            },
            AllowedTags = [.. CommentTextSanitizer.AllowedTags],
            AllowedAnchorAttributes = [.. CommentTextSanitizer.AllowedAnchorAttributes],
            Attachments = new AttachmentRules
            {
                ImageContentTypes = [.. ImageTypes.Select(t => t.ContentType)],
                ImageExtensions = [.. ImageTypes.SelectMany(t => t.Extensions)],
                MaxImageUploadBytes = Attachment.MaxImageUploadBytes,
                MaxImageWidth = Attachment.MaxImageWidth,
                MaxImageHeight = Attachment.MaxImageHeight,
                TextExtensions = [.. TextTypes.SelectMany(t => t.Extensions)],
                MaxTextFileBytes = Attachment.MaxTextFileBytes,
            },
            PageSize = Paging.DefaultPageSize,
        });

    private static SniffedFileType[] TypesOf(AttachmentKind kind) =>
        [.. FileTypeSniffer.SupportedTypes.Where(t => t.Kind == kind)];
}
