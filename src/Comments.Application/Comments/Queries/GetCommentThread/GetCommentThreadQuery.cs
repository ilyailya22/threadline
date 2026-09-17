using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Comments;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetCommentThread;

/// <summary>
/// Loads a whole thread — the top-level comment and every reply below it, nested for cascading
/// display.
/// </summary>
/// <param name="RootId">Id of the top-level comment.</param>
/// <param name="MaxDepth">
/// How many levels to materialise in one response. The UI loads the first levels eagerly and
/// fetches deeper branches on demand, so a pathological thread cannot produce a huge payload.
/// </param>
public sealed record GetCommentThreadQuery(Guid RootId, int MaxDepth = 10)
    : IRequest<CommentThreadDto>;

public sealed class GetCommentThreadQueryHandler(ICommentReadRepository repository)
    : IRequestHandler<GetCommentThreadQuery, CommentThreadDto>
{
    public async Task<CommentThreadDto> Handle(
        GetCommentThreadQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var depth = Math.Clamp(request.MaxDepth, 1, CommentPath.MaxDepth);

        return await repository.GetThreadAsync(request.RootId, depth, cancellationToken)
            ?? throw new NotFoundException("Comment", request.RootId);
    }
}
