using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Comments.Queries.GetReplies;
using MediatR;

namespace Threadline.Comments.Api.GraphQL;

/// <summary>Batches "give me the replies of these parents" into one query per level.</summary>
public sealed class RepliesByParentDataLoader(
    ISender sender,
    IBatchScheduler batchScheduler,
    DataLoaderOptions options) : BatchDataLoader<Guid, IReadOnlyList<CommentNodeDto>>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CommentNodeDto>>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken) =>
        await sender.Send(new GetRepliesQuery(keys), cancellationToken);
}
