using Threadline.Comments.Infrastructure.Media;
using Shouldly;
using SkiaSharp;

namespace Threadline.Comments.UnitTests.Attachments;

/// <summary>
/// A phone stores its photographs the way the sensor saw them and records how to turn them in an
/// EXIF tag. Re-encoding the pixels discards that tag — which is a security feature — so the
/// rotation has to be baked into the pixels first, or every portrait photo is served on its side.
/// </summary>
public sealed class ImageOrientationTests
{
    /// <summary>The four orientations that matter here: two leave the axes alone, two swap them.</summary>
    [Theory]
    [InlineData(1, 40, 20)] // as stored
    [InlineData(3, 40, 20)] // upside down
    [InlineData(6, 20, 40)] // quarter turn clockwise
    [InlineData(8, 20, 40)] // quarter turn anticlockwise
    public async Task An_orientation_tag_turns_the_pixels(int orientation, int expectedWidth, int expectedHeight)
    {
        var jpeg = HalfRedJpeg(40, 20, orientation);

        var processed = await new SkiaImageProcessor()
            .DownscaleAsync(new MemoryStream(jpeg), maxWidth: 320, maxHeight: 240);

        processed.Width.ShouldBe(expectedWidth);
        processed.Height.ShouldBe(expectedHeight);
    }

    /// <summary>
    /// Swapped dimensions alone would also pass if the image were turned the wrong way, so this
    /// checks where the pixels actually land: the left half of a quarter-turn-clockwise photo
    /// belongs at the top.
    /// </summary>
    [Fact]
    public async Task A_quarter_turn_clockwise_puts_the_left_edge_on_top()
    {
        var jpeg = HalfRedJpeg(40, 20, orientation: 6);

        var processed = await new SkiaImageProcessor()
            .DownscaleAsync(new MemoryStream(jpeg), maxWidth: 320, maxHeight: 240);

        using var result = SKBitmap.Decode(processed.Content);

        IsRed(result.GetPixel(result.Width / 2, result.Height / 4)).ShouldBeTrue("the top should be red");
        IsRed(result.GetPixel(result.Width / 2, result.Height * 3 / 4)).ShouldBeFalse("the bottom should be blue");
    }

    private static bool IsRed(SKColor color) => color.Red > 128 && color.Blue < 128;

    /// <summary>A JPEG whose left half is red and right half blue, carrying the given EXIF orientation.</summary>
    private static byte[] HalfRedJpeg(int width, int height, int orientation)
    {
        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            canvas.DrawRect(0, 0, width / 2f, height, new SKPaint { Color = SKColors.Red });
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);

        return WithExifOrientation(encoded.ToArray(), orientation);
    }

    /// <summary>
    /// Splices an APP1 segment carrying nothing but the orientation tag into an encoded JPEG.
    /// </summary>
    /// <remarks>
    /// Skia can read EXIF but not write it, and the fixture has to be a file that really carries
    /// the tag — otherwise the test would only prove that the code handles the tag it invented.
    /// </remarks>
    private static byte[] WithExifOrientation(byte[] jpeg, int orientation)
    {
        // TIFF header, little-endian, first IFD at offset 8.
        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00,                                     // one entry
            0x12, 0x01,                                     // tag 0x0112: orientation
            0x03, 0x00,                                     // type 3: SHORT
            0x01, 0x00, 0x00, 0x00,                         // one value
            (byte)orientation, 0x00, 0x00, 0x00,            // the value, inline
            0x00, 0x00, 0x00, 0x00,                         // no next IFD
        ];

        byte[] header = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00]; // "Exif\0\0"

        var length = header.Length + tiff.Length + 2;
        byte[] segment = [0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF), .. header, .. tiff];

        // After the SOI marker, before everything else.
        return [.. jpeg[..2], .. segment, .. jpeg[2..]];
    }
}
