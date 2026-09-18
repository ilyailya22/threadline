using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetComment;

public sealed class GetCommentQueryHandler(ICommentReadRepository repository)
    : IRequestHandler<GetCommentQuery, CommentNodeDto?>
{
    public Task<CommentNodeDto?> Handle(GetCommentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return repository.GetByIdAsync(request.Id, cancellationToken);
    }
}
