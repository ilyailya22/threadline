namespace Threadline.Comments.Application.Comments.Dtos;

/// <summary>Result of accepting a new comment.</summary>
/// <remarks>
/// The attachments are included, still pending: the client renders the comment from this before
/// the search index has it, and the "ready" push that follows identifies the attachment by id. An
/// optimistic row without them has nothing for that push to update.
/// </remarks>
public sealed record CreateCommentResultDto(
    Guid Id,
    Guid RootId,
    Guid? ParentId,
    DateTimeOffset CreatedAt,
    string TextHtml,
    IReadOnlyList<AttachmentDto> Attachments);
