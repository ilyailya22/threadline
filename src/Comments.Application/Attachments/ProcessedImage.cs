namespace Threadline.Comments.Application.Attachments;

/// <summary>An image after downscaling, plus the thumbnail the lightbox opens from.</summary>
public sealed record ProcessedImage(
    byte[] Content,
    string ContentType,
    int Width,
    int Height,
    byte[] Thumbnail,
    string ThumbnailContentType);
