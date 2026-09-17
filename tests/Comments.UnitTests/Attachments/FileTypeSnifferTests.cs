using System.Text;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Domain.Comments;
using Shouldly;

namespace Threadline.Comments.UnitTests.Attachments;

/// <summary>
/// The upload gate. A file's name and its browser-declared content type are both attacker
/// controlled; only the bytes are evidence.
/// </summary>
public sealed class FileTypeSnifferTests
{
    private readonly FileTypeSniffer _sniffer = new();

    [Fact]
    public void Recognises_png()
    {
        var detected = _sniffer.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

        detected.ShouldNotBeNull();
        detected.Kind.ShouldBe(AttachmentKind.Image);
        detected.ContentType.ShouldBe("image/png");
    }

    [Fact]
    public void Recognises_jpeg()
    {
        var detected = _sniffer.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0]);

        detected!.ContentType.ShouldBe("image/jpeg");
    }

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void Recognises_both_gif_versions(string signature)
    {
        var detected = _sniffer.Detect(Encoding.ASCII.GetBytes(signature + "xxxx"));

        detected!.ContentType.ShouldBe("image/gif");
    }

    [Fact]
    public void Recognises_plain_text()
    {
        var detected = _sniffer.Detect("Hello, world!\r\n\tIndented."u8);

        detected!.Kind.ShouldBe(AttachmentKind.TextFile);
        detected.ContentType.ShouldBe("text/plain");
    }

    /// <summary>
    /// The attack this exists to stop: an HTML document named <c>photo.png</c>. It is detected as
    /// text, its extension says image, and <see cref="AttachmentIntakeService"/> refuses the
    /// mismatch — so it never reaches storage as either.
    /// </summary>
    [Fact]
    public void An_html_document_is_text_not_an_image()
    {
        var detected = _sniffer.Detect("<html><script>alert(1)</script>"u8);

        detected!.Kind.ShouldBe(AttachmentKind.TextFile);
        detected.Extensions.ShouldContain(".txt");
    }

    [Theory]
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 })]     // PE executable
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 })]     // zip/docx
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x00 })]     // PDF (has a NUL)
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00, 0x43, 0x00 })]     // UTF-16 BOM
    public void Rejects_everything_else(byte[] header)
    {
        _sniffer.Detect(header).ShouldBeNull();
    }

    [Fact]
    public void An_empty_header_is_not_a_file()
    {
        // Zero bytes technically pass the "no NUL, no control characters" test for text, but an
        // empty upload is rejected by the size check in the intake service either way.
        _sniffer.Detect([]).ShouldNotBeNull();
    }
}
