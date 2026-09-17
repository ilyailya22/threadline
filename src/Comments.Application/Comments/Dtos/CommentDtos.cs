using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Comments.Dtos;

/// <summary>Author fields as they appear next to a comment.</summary>
public sealed record AuthorDto(
    Guid Id,
    string UserName,
    string Email,
    string? HomePage);

/// <summary>A file attached to a comment, as the client needs it for the lightbox.</summary>
public sealed record AttachmentDto(
    Guid Id,
    AttachmentKind Kind,
    AttachmentStatus Status,
    string ContentType,
    string OriginalFileName,
    long SizeBytes,
    string Url,
    string? ThumbnailUrl,
    int? Width,
    int? Height);

/// <summary>
/// One row of the sortable, paged table of top-level comments.
/// </summary>
/// <remarks>
/// <see cref="ReplyCount"/> and <see cref="LastReplyAt"/> are maintained asynchronously in the
/// Elasticsearch read model. They are allowed to lag the write model by the length of one queue
/// hop — a reply counter that is a second stale costs nothing, while keeping it exact would mean
/// writing to the same row from every reply.
/// </remarks>
public sealed record CommentListItemDto(
    Guid Id,
    AuthorDto Author,
    string TextHtml,
    string TextPreview,
    DateTimeOffset CreatedAt,
    int ReplyCount,
    DateTimeOffset? LastReplyAt,
    IReadOnlyList<AttachmentDto> Attachments);

/// <summary>A node of the cascading reply tree.</summary>
public sealed record CommentNodeDto(
    Guid Id,
    Guid? ParentId,
    Guid RootId,
    int Depth,
    AuthorDto Author,
    string TextHtml,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AttachmentDto> Attachments)
{
    /// <summary>Direct replies, already in display order. Populated when a thread is loaded.</summary>
    public IReadOnlyList<CommentNodeDto> Replies { get; init; } = [];
}

/// <summary>A whole thread: the top-level comment and every descendant, nested.</summary>
public sealed record CommentThreadDto(CommentNodeDto Root, int TotalCount);

/// <summary>Result of accepting a new comment.</summary>
public sealed record CreateCommentResultDto(
    Guid Id,
    Guid RootId,
    Guid? ParentId,
    DateTimeOffset CreatedAt,
    string TextHtml);

/// <summary>Server-rendered preview of a message, used by the "preview without reload" feature.</summary>
public sealed record CommentPreviewDto(string TextHtml, string TextPlain);
