using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetReplies;

public sealed class GetRepliesQueryHandler(ICommentReadRepository repository)
    : IRequestHandler<GetRepliesQuery, IReadOnlyDictionary<Guid, IReadOnlyList<CommentNodeDto>>>
{
    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<CommentNodeDto>>> Handle(
        GetRepliesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return repository.GetRepliesByParentIdsAsync(request.ParentIds, cancellationToken);
    }
}
