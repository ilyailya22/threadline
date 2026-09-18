namespace Threadline.Comments.Api.Contracts;

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
