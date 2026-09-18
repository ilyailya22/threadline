namespace Threadline.Comments.Api.Contracts;

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
