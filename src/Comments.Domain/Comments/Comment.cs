using Threadline.Comments.Domain.Comments.Events;
using Threadline.Comments.Domain.Common;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Domain.Comments;

/// <summary>
/// A message in the thread. Aggregate root: a comment owns its attachments and is the only way to
/// create them, so "an attachment always belongs to exactly one comment" is an invariant the type
/// system enforces rather than a rule the database hopes for.
/// </summary>
/// <remarks>
/// Deliberately <em>not</em> on this entity: reply counters. Incrementing a counter on the parent
/// (or the thread root) turns every reply into a write to a row that popular threads share, which is
/// the classic hot-row contention failure at the 100k-users/24h target. Counts are maintained
/// asynchronously in the Elasticsearch read model instead — see <c>docs/ARCHITECTURE.md</c>.
/// </remarks>
public sealed class Comment : Entity
{
    private readonly List<Attachment> _attachments = [];

    private Comment()
    {
        // EF Core
    }

    private Comment(
        Guid id,
        User author,
        Comment? parent,
        CommentBody body,
        ClientFingerprint fingerprint,
        DateTimeOffset createdAt)
        : base(id)
    {
        Author = author;
        AuthorId = author.Id;
        Body = body;
        Fingerprint = fingerprint;
        CreatedAt = createdAt;

        if (parent is null)
        {
            ParentId = null;
            RootId = id;
            Path = CommentPath.ForRoot(id);
        }
        else
        {
            ParentId = parent.Id;
            RootId = parent.RootId;
            Path = CommentPath.ForReply(parent.Path, id);
        }

        Depth = Path.Depth;
    }

    public Guid AuthorId { get; private set; }

    public User Author { get; private set; } = null!;

    /// <summary><see langword="null"/> for a top-level ("заглавный") comment.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Id of the top-level comment this one belongs to; equals <see cref="Entity.Id"/> at the root.</summary>
    public Guid RootId { get; private set; }

    public CommentPath Path { get; private set; } = null!;

    /// <summary>Denormalised <c>Path.Depth</c> — stored so queries can filter without string maths.</summary>
    public int Depth { get; private set; }

    public CommentBody Body { get; private set; } = null!;

    public ClientFingerprint Fingerprint { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsTopLevel => ParentId is null;

    public IReadOnlyCollection<Attachment> Attachments => _attachments.AsReadOnly();

    /// <summary>Creates a top-level comment — one that appears in the sortable, paged table.</summary>
    public static Comment CreateRoot(
        User author,
        CommentBody body,
        ClientFingerprint fingerprint,
        DateTimeOffset createdAt,
        IEnumerable<Attachment>? attachments = null)
    {
        ArgumentNullException.ThrowIfNull(author);

        return Create(author, parent: null, body, fingerprint, createdAt, attachments);
    }

    /// <summary>Creates a reply. Any comment can be replied to, at any depth below the cap.</summary>
    public static Comment CreateReply(
        User author,
        Comment parent,
        CommentBody body,
        ClientFingerprint fingerprint,
        DateTimeOffset createdAt,
        IEnumerable<Attachment>? attachments = null)
    {
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(parent);

        return Create(author, parent, body, fingerprint, createdAt, attachments);
    }

    private static Comment Create(
        User author,
        Comment? parent,
        CommentBody body,
        ClientFingerprint fingerprint,
        DateTimeOffset createdAt,
        IEnumerable<Attachment>? attachments)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(fingerprint);

        // UUID v7 seeded with the creation time: the id is the sort key, the path segment and the
        // clustered key all at once, so a comment needs no extra sequence to be ordered.
        var comment = new Comment(Guid.CreateVersion7(createdAt), author, parent, body, fingerprint, createdAt);

        foreach (var attachment in attachments ?? [])
        {
            comment.Attach(attachment);
        }

        author.RecordPost(createdAt);

        comment.Raise(new CommentCreatedDomainEvent(
            comment.Id,
            comment.RootId,
            comment.ParentId,
            comment.AuthorId,
            comment.Depth,
            [.. comment._attachments.Select(a => a.Id)],
            createdAt));

        return comment;
    }

    private void Attach(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        // The assignment allows "a picture or a text file" per message — one file, not a gallery.
        if (_attachments.Count >= 1)
        {
            throw new DomainException("A comment can carry at most one attachment.");
        }

        attachment.AttachTo(Id);
        _attachments.Add(attachment);
    }
}
