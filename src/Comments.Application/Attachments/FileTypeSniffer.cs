using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Attachments;

/// <summary>
/// Decides what an uploaded file really is by looking at its leading bytes.
/// </summary>
/// <remarks>
/// The file name and the browser-supplied Content-Type are both attacker-controlled: a
/// <c>.png</c> that is really an HTML document with a script in it is the classic stored-XSS
/// upload. Only the magic bytes decide here, and the declared extension must then agree with them —
/// so <c>payload.html</c> renamed to <c>payload.png</c> is rejected either way.
/// </remarks>
public sealed class FileTypeSniffer : IFileTypeSniffer
{
    /// <summary>Bytes needed to recognise every supported signature.</summary>
    public const int HeaderSize = 16;

    public static readonly SniffedFileType Jpeg = new(AttachmentKind.Image, "image/jpeg", [".jpg", ".jpeg"]);
    public static readonly SniffedFileType Png = new(AttachmentKind.Image, "image/png", [".png"]);
    public static readonly SniffedFileType Gif = new(AttachmentKind.Image, "image/gif", [".gif"]);
    public static readonly SniffedFileType PlainText = new(AttachmentKind.TextFile, "text/plain", [".txt"]);

    /// <summary>Every type an upload may be — the single list the published validation rules come from.</summary>
    public static readonly IReadOnlyList<SniffedFileType> SupportedTypes = [Jpeg, Png, Gif, PlainText];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> Gif87Signature => "GIF87a"u8;

    private static ReadOnlySpan<byte> Gif89Signature => "GIF89a"u8;

    public SniffedFileType? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(PngSignature))
        {
            return Png;
        }

        if (header.StartsWith(JpegSignature))
        {
            return Jpeg;
        }

        if (header.StartsWith(Gif87Signature) || header.StartsWith(Gif89Signature))
        {
            return Gif;
        }

        return LooksLikeText(header) ? PlainText : null;
    }

    /// <summary>
    /// A .txt file has no signature, so "is it text" is decided by exclusion: no NUL bytes, no
    /// control characters other than tab/CR/LF, and no UTF-16/UTF-32 byte-order mark (which would
    /// smuggle a NUL-free binary past the check).
    /// </summary>
    private static bool LooksLikeText(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 2
            && ((header[0] == 0xFF && header[1] == 0xFE) || (header[0] == 0xFE && header[1] == 0xFF)))
        {
            return false;
        }

        foreach (var b in header)
        {
            if (b == 0)
            {
                return false;
            }

            if (b is < 0x20 and not ((byte)'\t' or (byte)'\n' or (byte)'\r'))
            {
                return false;
            }
        }

        return true;
    }
}
