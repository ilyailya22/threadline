using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetCommentThread;

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
        var after = ParseCursor(request.After);

        return await repository.GetThreadAsync(request.RootId, depth, limit, after, cancellationToken)
            ?? throw new NotFoundException(nameof(Comment), request.RootId);
    }

    /// <summary>
    /// The cursor arrives from a client, so it is parsed rather than passed to a query as a raw
    /// string: anything malformed is a 400 here instead of a strange comparison in SQL.
    /// </summary>
    private static ThreadCursor? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        return ThreadCursor.TryParse(cursor, out var parsed)
            ? parsed
            : throw new InputValidationException("after", "The cursor is not valid.");
    }
}
