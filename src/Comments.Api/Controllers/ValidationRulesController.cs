using Threadline.Comments.Application.Comments.Sanitization;
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
                Pattern = "^[A-Za-z0-9]{1,16}$",
                MaxLength = 16,
                Description = "Latin letters and digits only.",
            },
            AllowedTags = [.. CommentTextSanitizer.AllowedTags],
            AllowedAnchorAttributes = [.. CommentTextSanitizer.AllowedAnchorAttributes],
            Attachments = new AttachmentRules
            {
                ImageContentTypes = ["image/jpeg", "image/png", "image/gif"],
                ImageExtensions = [".jpg", ".jpeg", ".png", ".gif"],
                MaxImageUploadBytes = Attachment.MaxImageUploadBytes,
                MaxImageWidth = Attachment.MaxImageWidth,
                MaxImageHeight = Attachment.MaxImageHeight,
                TextExtensions = [".txt"],
                MaxTextFileBytes = Attachment.MaxTextFileBytes,
            },
            PageSize = Application.Common.Models.Paging.DefaultPageSize,
        });
}

public sealed record FieldRules
{
    public bool Required { get; init; }

    public string? Pattern { get; init; }

    public int? MinLength { get; init; }

    public int? MaxLength { get; init; }

    public string? Description { get; init; }
}

public sealed record AttachmentRules
{
    public IReadOnlyList<string> ImageContentTypes { get; init; } = [];

    public IReadOnlyList<string> ImageExtensions { get; init; } = [];

    public long MaxImageUploadBytes { get; init; }

    public int MaxImageWidth { get; init; }

    public int MaxImageHeight { get; init; }

    public IReadOnlyList<string> TextExtensions { get; init; } = [];

    public long MaxTextFileBytes { get; init; }
}

public sealed record ValidationRulesResponse
{
    public required FieldRules UserName { get; init; }

    public required FieldRules Email { get; init; }

    public required FieldRules HomePage { get; init; }

    public required FieldRules Text { get; init; }

    public required FieldRules Captcha { get; init; }

    public IReadOnlyList<string> AllowedTags { get; init; } = [];

    public IReadOnlyList<string> AllowedAnchorAttributes { get; init; } = [];

    public required AttachmentRules Attachments { get; init; }

    public int PageSize { get; init; }
}
