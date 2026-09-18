namespace Threadline.Comments.Application.Attachments;

/// <summary>
/// An image after downscaling, plus the thumbnail the lightbox opens from, encoded as
/// <see cref="ProcessedImageFormat"/> specifies.
/// </summary>
public sealed record ProcessedImage(
    byte[] Content,
    int Width,
    int Height,
    byte[] Thumbnail);
