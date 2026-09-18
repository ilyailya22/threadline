using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Attachments.Queries.GetAttachmentContent;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Comments;
using Moq;
using Shouldly;

namespace Threadline.Comments.UnitTests.Attachments;

public sealed class GetAttachmentContentQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IAttachmentRepository> _attachments = new();
    private readonly Mock<IFileStorage> _storage = new();

    [Fact]
    public async Task A_processed_image_is_served_as_the_png_it_was_re_encoded_to_and_may_be_cached()
    {
        var image = Attachment.CreateImage("image/jpeg", "photo.jpg", 4096, "originals/p.jpg", Now);
        image.MarkImageProcessed("files/p.png", ProcessedImageFormat.DisplayContentType, "files/p-thumb.webp", 320, 240, 2048, Now);
        Stored(image, "files/p.png");

        var file = await Handle(image.Id, thumbnail: false);

        file.ContentType.ShouldBe("image/png");
        file.IsDownload.ShouldBeFalse();
        file.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public async Task An_image_still_being_processed_is_served_but_not_cacheable()
    {
        var image = Attachment.CreateImage("image/jpeg", "photo.jpg", 4096, "originals/p.jpg", Now);
        Stored(image, "originals/p.jpg");

        var file = await Handle(image.Id, thumbnail: false);

        file.IsFinal.ShouldBeFalse();
    }

    [Fact]
    public async Task A_text_file_is_always_a_download_so_it_can_never_render_as_html()
    {
        var text = Attachment.CreateTextFile("notes.txt", 100, "files/n.txt", Now);
        Stored(text, "files/n.txt");

        var file = await Handle(text.Id, thumbnail: false);

        file.IsDownload.ShouldBeTrue();
        file.DownloadFileName.ShouldBe("notes.txt");
    }

    [Fact]
    public async Task A_thumbnail_is_served_as_webp()
    {
        var image = Attachment.CreateImage("image/png", "photo.png", 4096, "originals/p.png", Now);
        image.MarkImageProcessed("files/p.png", ProcessedImageFormat.DisplayContentType, "files/p-thumb.webp", 320, 240, 2048, Now);
        Stored(image, "files/p-thumb.webp");

        var file = await Handle(image.Id, thumbnail: true);

        file.ContentType.ShouldBe(ProcessedImageFormat.ThumbnailContentType);
    }

    [Fact]
    public async Task A_text_file_has_no_thumbnail()
    {
        var text = Attachment.CreateTextFile("notes.txt", 100, "files/n.txt", Now);
        _attachments.Setup(a => a.FindAsync(text.Id, It.IsAny<CancellationToken>())).ReturnsAsync(text);

        await Should.ThrowAsync<NotFoundException>(() => Handle(text.Id, thumbnail: true));
    }

    [Fact]
    public async Task An_unknown_attachment_is_not_found() =>
        await Should.ThrowAsync<NotFoundException>(() => Handle(Guid.CreateVersion7(), thumbnail: false));

    [Fact]
    public async Task An_attachment_whose_blob_is_missing_is_not_found()
    {
        var text = Attachment.CreateTextFile("notes.txt", 100, "files/n.txt", Now);
        _attachments.Setup(a => a.FindAsync(text.Id, It.IsAny<CancellationToken>())).ReturnsAsync(text);

        await Should.ThrowAsync<NotFoundException>(() => Handle(text.Id, thumbnail: false));
    }

    private void Stored(Attachment attachment, string path)
    {
        _attachments.Setup(a => a.FindAsync(attachment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attachment);
        _storage.Setup(s => s.OpenReadAsync(path, It.IsAny<CancellationToken>())).ReturnsAsync(new MemoryStream([1, 2, 3]));
    }

    private Task<AttachmentContent> Handle(Guid id, bool thumbnail) =>
        new GetAttachmentContentQueryHandler(_attachments.Object, _storage.Object)
            .Handle(new GetAttachmentContentQuery(id, thumbnail), CancellationToken.None);
}
