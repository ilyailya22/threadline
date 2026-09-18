namespace Threadline.Comments.Application.Attachments;

/// <summary>
/// The formats every processed image is stored in, whatever it was uploaded as. One definition for
/// the processor that encodes them, the worker that names the blobs and the endpoint that serves them.
/// </summary>
public static class ProcessedImageFormat
{
    /// <summary>PNG for the 320×240 display image: lossless, so downscaling is the only quality loss.</summary>
    public const string DisplayContentType = "image/png";

    public const string DisplayExtension = ".png";

    /// <summary>
    /// WebP for the thumbnail: it is the asset repeated on every row of the page, so the ~30% saving
    /// over PNG is the single biggest win available on page weight.
    /// </summary>
    public const string ThumbnailContentType = "image/webp";

    public const string ThumbnailExtension = ".webp";
}
