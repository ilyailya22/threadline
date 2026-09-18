using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Common;
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

public sealed class GetCommentThreadQueryHandler(ICommentReadRepository repository)
    : IRequestHandler<GetCommentThreadQuery, CommentThreadDto>
{
    public async Task<CommentThreadDto> Handle(
        GetCommentThreadQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var depth = Math.Clamp(request.MaxDepth, 1, CommentPath.MaxDepth);
        var limit = Math.Clamp(request.Limit, 1, GetCommentThreadQuery.MaxLimit);

        // The cursor is a materialised path, but it arrives from a client: it is parsed through the
        // domain type rather than passed to a query as a raw string, so anything that is not a
        // well-formed path is a 400 here instead of a strange comparison in SQL.
        string? after = null;

        if (!string.IsNullOrWhiteSpace(request.After))
        {
            try
            {
                after = CommentPath.FromStorage(request.After).Value;
            }
            catch (DomainException)
            {
                throw new InputValidationException("after", "The cursor is not valid.");
            }
        }

        return await repository.GetThreadAsync(request.RootId, depth, limit, after, cancellationToken)
            ?? throw new NotFoundException("Comment", request.RootId);
    }
}
