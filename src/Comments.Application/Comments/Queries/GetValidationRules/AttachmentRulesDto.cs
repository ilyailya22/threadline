namespace Threadline.Comments.Application.Comments.Queries.GetValidationRules;

/// <summary>What a file must be to be accepted as an attachment.</summary>
public sealed record AttachmentRulesDto
{
    public IReadOnlyList<string> ImageContentTypes { get; init; } = [];

    public IReadOnlyList<string> ImageExtensions { get; init; } = [];

    public long MaxImageUploadBytes { get; init; }

    public int MaxImageWidth { get; init; }

    public int MaxImageHeight { get; init; }

    public IReadOnlyList<string> TextExtensions { get; init; } = [];

    public long MaxTextFileBytes { get; init; }
}
