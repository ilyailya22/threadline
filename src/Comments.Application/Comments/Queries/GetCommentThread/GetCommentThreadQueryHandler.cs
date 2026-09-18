using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Common;
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
    /// The cursor is a materialised path, but it arrives from a client: it is parsed through the
    /// domain type rather than passed to a query as a raw string, so anything that is not a
    /// well-formed path is a 400 here instead of a strange comparison in SQL.
    /// </summary>
    private static string? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            return CommentPath.FromStorage(cursor).Value;
        }
        catch (DomainException)
        {
            throw new InputValidationException("after", "The cursor is not valid.");
        }
    }
}
