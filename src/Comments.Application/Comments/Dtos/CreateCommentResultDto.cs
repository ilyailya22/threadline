namespace Threadline.Comments.Application.Comments.Dtos;

/// <summary>Result of accepting a new comment.</summary>
public sealed record CreateCommentResultDto(
    Guid Id,
    Guid RootId,
    Guid? ParentId,
    DateTimeOffset CreatedAt,
    string TextHtml);
