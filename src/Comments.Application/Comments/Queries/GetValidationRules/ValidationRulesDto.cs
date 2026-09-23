namespace Threadline.Comments.Application.Comments.Queries.GetValidationRules;

/// <summary>Every rule the comment form is validated against, as published to the client.</summary>
public sealed record ValidationRulesDto
{
    public required FieldRulesDto UserName { get; init; }

    public required FieldRulesDto Email { get; init; }

    public required FieldRulesDto HomePage { get; init; }

    public required FieldRulesDto Text { get; init; }

    public required FieldRulesDto Captcha { get; init; }

    public IReadOnlyList<string> AllowedTags { get; init; } = [];

    public IReadOnlyList<string> AllowedAnchorAttributes { get; init; } = [];

    public required AttachmentRulesDto Attachments { get; init; }

    /// <summary>What a password must be, so the sign-up form checks what the server checks.</summary>
    public required FieldRulesDto Password { get; init; }

    public int PageSize { get; init; }
}
