using Threadline.Comments.Application.Accounts;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Comments.Sanitization;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetValidationRules;

/// <summary>
/// Assembles the rules from where they are defined. Nothing here is a literal limit or pattern —
/// every value is a reference, so this cannot drift from what the server actually checks.
/// </summary>
public sealed class GetValidationRulesQueryHandler : IRequestHandler<GetValidationRulesQuery, ValidationRulesDto>
{
    /// <summary>The rules only change with a deployment, so they are built once.</summary>
    private static readonly ValidationRulesDto Rules = Build();

    public Task<ValidationRulesDto> Handle(GetValidationRulesQuery request, CancellationToken cancellationToken) =>
        Task.FromResult(Rules);

    private static ValidationRulesDto Build()
    {
        var images = TypesOf(AttachmentKind.Image);
        var textFiles = TypesOf(AttachmentKind.TextFile);

        return new ValidationRulesDto
        {
            UserName = new FieldRulesDto
            {
                Required = true,
                Pattern = UserName.Pattern,
                MinLength = UserName.MinLength,
                MaxLength = UserName.MaxLength,
                Description = "Latin letters and digits only.",
            },
            Email = new FieldRulesDto
            {
                Required = true,
                Pattern = EmailAddress.Pattern,
                MaxLength = EmailAddress.MaxLength,
                Description = "A valid e-mail address.",
            },
            HomePage = new FieldRulesDto
            {
                Required = false,
                MaxLength = HomePageUrl.MaxLength,
                Description = "Absolute http or https URL.",
            },
            Text = new FieldRulesDto
            {
                Required = true,
                MinLength = 1,
                MaxLength = CommentBody.MaxHtmlLength,
                Description = "Only the allowed tags survive; every tag must be closed.",
            },
            Captcha = new FieldRulesDto
            {
                Required = true,
                Pattern = CaptchaAnswerFormat.Pattern,
                MaxLength = CaptchaAnswerFormat.MaxLength,
                Description = "Latin letters and digits only.",
            },
            AllowedTags = [.. CommentTextSanitizer.AllowedTags],
            AllowedAnchorAttributes = [.. CommentTextSanitizer.AllowedAnchorAttributes],
            Attachments = new AttachmentRulesDto
            {
                ImageContentTypes = [.. images.Select(t => t.ContentType)],
                ImageExtensions = [.. images.SelectMany(t => t.Extensions)],
                MaxImageUploadBytes = Attachment.MaxImageUploadBytes,
                MaxImageWidth = Attachment.MaxImageWidth,
                MaxImageHeight = Attachment.MaxImageHeight,
                TextExtensions = [.. textFiles.SelectMany(t => t.Extensions)],
                MaxTextFileBytes = Attachment.MaxTextFileBytes,
            },
            Password = new FieldRulesDto
            {
                Required = true,
                MinLength = PasswordPolicy.MinLength,
                MaxLength = PasswordPolicy.MaxLength,
                Description = $"At least {PasswordPolicy.MinLength} characters. Length is the only rule.",
            },
            PageSize = Paging.DefaultPageSize,
        };
    }

    private static SniffedFileType[] TypesOf(AttachmentKind kind) =>
        [.. FileTypeSniffer.SupportedTypes.Where(t => t.Kind == kind)];
}
