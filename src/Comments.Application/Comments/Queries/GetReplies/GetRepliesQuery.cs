using Threadline.Comments.Application.Comments.Dtos;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetReplies;

/// <summary>
/// Direct replies of several comments at once, keyed by parent id. Batched so that a GraphQL query
/// over a page of comments costs one query per level rather than one per comment.
/// </summary>
public sealed record GetRepliesQuery(IReadOnlyList<Guid> ParentIds)
    : IRequest<IReadOnlyDictionary<Guid, IReadOnlyList<CommentNodeDto>>>;
