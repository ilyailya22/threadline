namespace Threadline.Comments.Application.Attachments;

/// <summary>
/// Downscales an uploaded image to the display size the assignment specifies.
/// </summary>
/// <remarks>
/// The assignment is explicit: an image must be at most 320×240 and a larger one must be scaled
/// down <em>proportionally</em>, not cropped or squashed. Implementations must therefore preserve
/// the aspect ratio and must never upscale an image that is already smaller.
/// </remarks>
public interface IImageProcessor
{
    Task<ProcessedImage> DownscaleAsync(
        Stream source,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken = default);
}
