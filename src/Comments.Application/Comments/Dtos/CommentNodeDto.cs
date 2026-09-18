namespace Threadline.Comments.Application.Comments.Dtos;

/// <summary>
/// One comment of a thread. Flat: the nesting is carried by <see cref="ParentId"/>, and the client
/// (or GraphQL's <c>replies</c> field) assembles the tree.
/// </summary>
public sealed record CommentNodeDto(
    Guid Id,
    Guid? ParentId,
    Guid RootId,
    int Depth,
    AuthorDto Author,
    string TextHtml,
    DateTimeOffset CreatedAt,
    IReadOnlyList<AttachmentDto> Attachments);
