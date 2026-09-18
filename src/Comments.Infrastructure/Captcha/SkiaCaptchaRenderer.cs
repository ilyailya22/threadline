using System.Reflection;
using System.Security.Cryptography;
using Threadline.Comments.Application.Captcha;
using SkiaSharp;

namespace Threadline.Comments.Infrastructure.Captcha;

/// <summary>
/// Draws the CAPTCHA challenge as a distorted PNG using SkiaSharp.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why SkiaSharp and not ImageSharp.</b> SixLabors moved ImageSharp 3.x and ImageSharp.Drawing
/// to a commercial licence — the build literally emits "No Six Labors license found… obtain a
/// licence". SkiaSharp is MIT, is the engine behind Chrome's rendering, and covers both jobs this
/// system needs (text rasterisation here, image resizing in the worker), so it replaces both
/// packages rather than adding a third.
/// </para>
/// <para>
/// <b>Why the font is embedded.</b> The <c>mcr.microsoft.com/dotnet/aspnet</c> image ships with no
/// fonts and no fontconfig. Resolving a system typeface would work on a developer's Windows machine
/// and return null in production — a failure that only appears after deployment. The typeface is
/// loaded from an embedded resource, so the container needs nothing installed.
/// </para>
/// <para>
/// <b>Why the distortions.</b> Per-character rotation, vertical jitter, varying size and colour,
/// overlapping noise curves and speckles defeat naive segmentation-then-OCR, which is what a
/// commodity CAPTCHA breaker does. Every parameter comes from a cryptographic RNG, so two renders
/// of the same code never produce the same image and a rendered-image lookup table is useless.
/// </para>
/// </remarks>
public sealed class SkiaCaptchaRenderer : ICaptchaImageRenderer, IDisposable
{
    private const int Width = 220;
    private const int Height = 70;

    private static readonly SKColor Background = new(0xF4, 0xF6, 0xFB);

    private static readonly SKColor[] InkPalette =
    [
        new(0x1B, 0x3A, 0x6B),
        new(0x34, 0x49, 0x5E),
        new(0x4A, 0x2C, 0x6B),
        new(0x0F, 0x51, 0x32),
    ];

    private readonly SKTypeface _typeface;

    public SkiaCaptchaRenderer()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Threadline.Comments.Infrastructure.Captcha.Fonts.RobotoMono.ttf")
            ?? throw new InvalidOperationException("Embedded CAPTCHA font was not found.");

        // Skia needs a seekable stream it can keep; the resource stream is copied into memory once
        // at startup and the typeface is then reused for the lifetime of the singleton.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        _typeface = SKTypeface.FromStream(buffer)
            ?? throw new InvalidOperationException("Embedded CAPTCHA font could not be parsed.");
    }

    public byte[] Render(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        using var surface = SKSurface.Create(new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        canvas.Clear(Background);

        DrawNoiseCurves(canvas);
        DrawCharacters(canvas, code);
        DrawSpeckles(canvas);

        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);

        return data.ToArray();
    }

    public void Dispose() => _typeface.Dispose();

    private void DrawCharacters(SKCanvas canvas, string code)
    {
        var slot = (float)Width / (code.Length + 1);

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };

        for (var i = 0; i < code.Length; i++)
        {
            using var font = new SKFont(_typeface, RandomFloat(34, 46))
            {
                Embolden = true,
                // A per-character horizontal skew bends the glyph itself, on top of the rotation
                // applied to the canvas below.
                SkewX = RandomFloat(-0.25f, 0.25f),
            };

            paint.Color = InkPalette[RandomNumberGenerator.GetInt32(InkPalette.Length)];

            var x = (slot * (i + 1)) + RandomFloat(-4, 4);
            var y = (Height / 2f) + (font.Size / 3f) + RandomFloat(-6, 6);

            canvas.Save();
            canvas.RotateDegrees(RandomFloat(-24, 24), x, y);
            canvas.DrawText(code[i].ToString(), x, y, SKTextAlign.Center, font, paint);
            canvas.Restore();
        }
    }

    private static void DrawNoiseCurves(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };

        for (var i = 0; i < 5; i++)
        {
            paint.Color = InkPalette[RandomNumberGenerator.GetInt32(InkPalette.Length)]
                .WithAlpha((byte)RandomNumberGenerator.GetInt32(40, 90));
            paint.StrokeWidth = RandomFloat(1f, 2.5f);

            using var builder = new SKPathBuilder();
            builder.MoveTo(RandomFloat(0, Width), RandomFloat(0, Height));
            builder.CubicTo(
                RandomFloat(0, Width), RandomFloat(0, Height),
                RandomFloat(0, Width), RandomFloat(0, Height),
                RandomFloat(0, Width), RandomFloat(0, Height));

            using var path = builder.Snapshot();
            canvas.DrawPath(path, paint);
        }
    }

    private static void DrawSpeckles(SKCanvas canvas)
    {
        using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };

        for (var i = 0; i < 140; i++)
        {
            paint.Color = InkPalette[RandomNumberGenerator.GetInt32(InkPalette.Length)]
                .WithAlpha((byte)RandomNumberGenerator.GetInt32(30, 110));

            canvas.DrawRect(RandomFloat(0, Width), RandomFloat(0, Height), 2, 2, paint);
        }
    }

    private static float RandomFloat(float min, float max) =>
        min + ((max - min) * (RandomNumberGenerator.GetInt32(0, 10_001) / 10_000f));
}
