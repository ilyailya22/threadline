using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Comments.Events;
using Threadline.Comments.Domain.Common;
using Threadline.Comments.Domain.Users;
using Shouldly;

namespace Threadline.Comments.UnitTests.Domain;

public sealed class CommentTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 22, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_root_comment_is_its_own_thread_root()
    {
        var comment = CreateRoot();

        comment.IsTopLevel.ShouldBeTrue();
        comment.ParentId.ShouldBeNull();
        comment.RootId.ShouldBe(comment.Id);
        comment.Depth.ShouldBe(1);
    }

    [Fact]
    public void A_reply_inherits_the_thread_root_from_its_parent()
    {
        var root = CreateRoot();
        var reply = CreateReply(root);
        var nested = CreateReply(reply);

        reply.RootId.ShouldBe(root.Id);
        nested.RootId.ShouldBe(root.Id);
        nested.ParentId.ShouldBe(reply.Id);
        nested.Depth.ShouldBe(3);
    }

    [Fact]
    public void Creating_a_comment_raises_exactly_one_event()
    {
        var comment = CreateRoot();

        var raised = comment.DomainEvents.OfType<CommentCreatedDomainEvent>().ToArray();

        raised.Length.ShouldBe(1);
        raised[0].CommentId.ShouldBe(comment.Id);
        raised[0].RootId.ShouldBe(comment.Id);
        raised[0].ParentId.ShouldBeNull();
    }

    /// <summary>
    /// The outbox depends on this: events must be cleared once converted, or the same side effect
    /// is scheduled again on the next save of the same tracked entity.
    /// </summary>
    [Fact]
    public void Events_can_be_cleared_once_collected()
    {
        var comment = CreateRoot();

        comment.ClearDomainEvents();

        comment.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void An_id_encodes_its_creation_time()
    {
        var comment = CreateRoot();

        // UUID v7: the first 48 bits are the timestamp, so ordering by id is ordering by time.
        var later = Comment.CreateRoot(Author(), Body(), Fingerprint(), Now.AddHours(1));

        string.CompareOrdinal(comment.Id.ToString("N"), later.Id.ToString("N")).ShouldBeLessThan(0);
    }

    [Fact]
    public void A_comment_accepts_one_attachment()
    {
        var attachment = Attachment.CreateImage("image/png", "photo.png", 2048, "files/x.png", "thumbnails/x.webp", 320, 240, Now);

        var comment = Comment.CreateRoot(Author(), Body(), Fingerprint(), Now, [attachment]);

        comment.Attachments.Count.ShouldBe(1);
        comment.Attachments.Single().CommentId.ShouldBe(comment.Id);
    }

    [Fact]
    public void A_second_attachment_is_refused()
    {
        var first = Attachment.CreateImage("image/png", "a.png", 1024, "files/a.png", "thumbnails/a.webp", 320, 240, Now);
        var second = Attachment.CreateImage("image/png", "b.png", 1024, "files/b.png", "thumbnails/b.webp", 320, 240, Now);

        Should.Throw<DomainException>(() =>
            Comment.CreateRoot(Author(), Body(), Fingerprint(), Now, [first, second]));
    }

    [Fact]
    public void Posting_updates_the_authors_last_activity()
    {
        var author = Author();
        var before = author.LastPostedAt;

        Comment.CreateRoot(author, Body(), Fingerprint(), Now.AddDays(3));

        author.LastPostedAt.ShouldBeGreaterThan(before);
    }

    private static Comment CreateRoot() => Comment.CreateRoot(Author(), Body(), Fingerprint(), Now);

    private static Comment CreateReply(Comment parent) =>
        Comment.CreateReply(Author(), parent, Body(), Fingerprint(), Now.AddMinutes(5));

    private static User Author() =>
        User.Register(
            UserName.Create("Anonym"),
            EmailAddress.Create("anonym@example.com"),
            homePage: null,
            Now.AddDays(-1));

    private static CommentBody Body() => CommentBody.FromSanitized("<strong>Hi</strong>", "Hi");

    private static ClientFingerprint Fingerprint() =>
        ClientFingerprint.Create(new string('a', ClientFingerprint.IpHashLength), "test-agent", Guid.CreateVersion7());
}

public sealed class AttachmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 22, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_text_file_over_100_kb_is_refused()
    {
        Should.Throw<DomainException>(() =>
            Attachment.CreateTextFile("notes.txt", Attachment.MaxTextFileBytes + 1, "originals/n.txt", Now));
    }

    [Fact]
    public void A_text_file_at_exactly_100_kb_is_accepted()
    {
        var attachment = Attachment.CreateTextFile(
            "notes.txt",
            Attachment.MaxTextFileBytes,
            "originals/n.txt",
            Now);

        attachment.Kind.ShouldBe(AttachmentKind.TextFile);
        attachment.ThumbnailPath.ShouldBeNull();
    }

    /// <summary>
    /// The domain refuses an image that does not satisfy the assignment's 320×240 rule, so a bug in
    /// the resizer becomes a rejected upload rather than an oversized picture on the page.
    /// </summary>
    [Fact]
    public void An_oversized_image_is_refused()
    {
        Should.Throw<DomainException>(() => Attachment.CreateImage(
            "image/png", "photo.png", 2048, "files/p.png", "thumbnails/p.webp", 640, 480, Now));
    }

    [Fact]
    public void A_stored_image_is_servable_the_moment_it_exists()
    {
        var attachment = Attachment.CreateImage(
            "image/png", "photo.jpg", 20_000, "files/p.png", "thumbnails/p.webp", 320, 240, Now);

        attachment.Width.ShouldBe(320);
        attachment.Height.ShouldBe(240);
        attachment.ThumbnailPath.ShouldNotBeNull();
        attachment.SizeBytes.ShouldBe(20_000);

        // The stored file is the re-encoded PNG, whatever was uploaded.
        attachment.ContentType.ShouldBe("image/png");
    }

    /// <summary>
    /// A file name is display data. Neutralising it in the domain means no call site has to
    /// remember that <c>../../web.config</c> is a thing users type.
    /// </summary>
    [Theory]
    [InlineData("../../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\config", "config")]
    [InlineData("/absolute/path/photo.png", "photo.png")]
    public void Directory_components_are_stripped_from_the_file_name(string input, string expected)
    {
        var attachment = Attachment.CreateTextFile(input, 128, "originals/x.txt", Now);

        attachment.OriginalFileName.ShouldBe(expected);
    }

    [Fact]
    public void A_file_name_that_is_only_a_path_is_refused() => Should.Throw<DomainException>(() => Attachment.CreateTextFile("../", 128, "originals/x.txt", Now));
}
