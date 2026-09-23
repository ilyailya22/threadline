using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Common.Exceptions;
using SkiaSharp;

namespace Threadline.Comments.Infrastructure.Media;

/// <summary>
/// Downscales uploaded images with SkiaSharp.
/// </summary>
/// <remarks>
/// <para>
/// Re-encoding rather than copying is a security measure as much as a formatting one. Decoding to
/// pixels and encoding afresh discards EXIF, colour profiles and any data appended after the image
/// payload — so a "polyglot" file that is a valid PNG <em>and</em> a valid HTML document comes out
/// the other side as nothing but pixels.
/// </para>
/// <para>
/// Discarding EXIF is why the orientation in it has to be applied first. A phone photograph is
/// almost always stored in the sensor's own orientation with a tag saying how to turn it; drop the
/// tag without turning the pixels and every portrait photo is served on its side.
/// </para>
/// <para>
/// The assignment requires proportional downscaling to at most 320×240 and says nothing about
/// enlarging, so an image that already fits is re-encoded at its own size and never upscaled.
/// </para>
/// </remarks>
public sealed class SkiaImageProcessor : IImageProcessor
{
    private const int ThumbnailWidth = 160;
    private const int ThumbnailHeight = 120;

    /// <summary>
    /// Pixel ceiling checked <em>before</em> decoding. A 60 KB PNG can declare 40000×40000 pixels
    /// and expand to gigabytes of bitmap; reading the header first turns that from an
    /// out-of-memory crash into one rejected attachment.
    /// </summary>
    private const long MaxPixels = 64L * 1024 * 1024;

    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public async Task<ProcessedImage> DownscaleAsync(
        Stream source,
        int maxWidth,
        int maxHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 1);

        var bytes = await ReadAllAsync(source, cancellationToken);

        using var data = SKData.CreateCopy(bytes);

        SKEncodedOrigin origin;

        using (var codec = SKCodec.Create(data))
        {
            if (codec is null)
            {
                throw new InputValidationException("file", "The file is not a readable image.");
            }

            var info = codec.Info;

            if ((long)info.Width * info.Height > MaxPixels)
            {
                throw new InputValidationException(
                    "file",
                    $"The image is too large to process ({info.Width}×{info.Height} pixels).");
            }

            origin = codec.EncodedOrigin;
        }

        using var decoded = SKBitmap.Decode(data)
            ?? throw new InputValidationException("file", "The image could not be decoded.");

        using var upright = Upright(decoded, origin);

        var (width, height) = Fit(upright.Width, upright.Height, maxWidth, maxHeight);

        using var resized = Resize(upright, width, height);
        var content = Encode(resized, SKEncodedImageFormat.Png, 100);

        var (thumbWidth, thumbHeight) = Fit(width, height, ThumbnailWidth, ThumbnailHeight);
        using var thumbnail = Resize(resized, thumbWidth, thumbHeight);

        var thumbnailBytes = Encode(thumbnail, SKEncodedImageFormat.Webp, 80);

        // Encoded as ProcessedImageFormat promises: PNG for display, WebP for the thumbnail.
        return new ProcessedImage(content, width, height, thumbnailBytes);
    }

    /// <summary>
    /// Turns the pixels the way the EXIF orientation says they should be shown.
    /// </summary>
    /// <remarks>
    /// The eight orientations are the four rotations and their mirrored twins. Four of them swap
    /// width and height, which is why the target bitmap is allocated from the mapped size rather
    /// than the source's.
    /// </remarks>
    internal static SKBitmap Upright(SKBitmap source, SKEncodedOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (origin is SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft)
        {
            return source.Copy();
        }

        float w = source.Width;
        float h = source.Height;

        // Each matrix maps a source pixel to where it belongs once the photo is the right way up.
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, w, 0, 0, 0, 1),
            _ => SKMatrix.Identity,
        };

        var swapsAxes = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        var target = new SKBitmap(new SKImageInfo(
            swapsAxes ? source.Height : source.Width,
            swapsAxes ? source.Width : source.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));

        using (var canvas = new SKCanvas(target))
        {
            canvas.SetMatrix(matrix);
            canvas.DrawBitmap(source, 0, 0, Sampling);
        }

        return target;
    }

    /// <summary>
    /// Scales <paramref name="width"/>×<paramref name="height"/> down to fit the box while keeping
    /// the aspect ratio. Returns the original size when it already fits — the assignment asks for
    /// shrinking, not stretching.
    /// </summary>
    internal static (int Width, int Height) Fit(int width, int height, int maxWidth, int maxHeight)
    {
        if (width <= maxWidth && height <= maxHeight)
        {
            return (width, height);
        }

        var scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);

        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        if (source.Width == width && source.Height == height)
        {
            return source.Copy();
        }

        var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

        if (!source.ScalePixels(target, Sampling))
        {
            target.Dispose();
            throw new InputValidationException("file", "The image could not be resized.");
        }

        return target;
    }

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, quality)
            ?? throw new InputValidationException("file", "The image could not be encoded.");

        return encoded.ToArray();
    }

    private static async Task<byte[]> ReadAllAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source is MemoryStream existing)
        {
            return existing.ToArray();
        }

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
