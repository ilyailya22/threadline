namespace Threadline.Comments.Application.Comments.Dtos;

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
