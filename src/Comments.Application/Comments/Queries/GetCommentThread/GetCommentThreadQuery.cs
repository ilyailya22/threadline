using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Domain.Comments;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetCommentThread;

/// <summary>
/// Loads one page of a thread — the top-level comment and its replies, depth-first, for cascading
/// display.
/// </summary>
/// <param name="RootId">Id of the top-level comment.</param>
/// <param name="MaxDepth">Deepest level to include.</param>
/// <param name="Limit">Comments per page. Bounded, because a thread is not.</param>
/// <param name="After">The previous page's <see cref="CommentThreadDto.NextCursor"/>; omit for the first page.</param>
public sealed record GetCommentThreadQuery(
    Guid RootId,
    int MaxDepth = CommentPath.MaxDepth,
    int Limit = GetCommentThreadQuery.DefaultLimit,
    string? After = null) : IRequest<CommentThreadDto>
{
    public const int DefaultLimit = 100;

    public const int MaxLimit = 500;
}
