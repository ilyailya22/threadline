using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Comments.Queries.GetCommentThread;
using Threadline.Comments.Application.Comments.Queries.GetTopLevelComments;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Models;
using GreenDonut;
using MediatR;

namespace Threadline.Comments.Api.GraphQL;

/// <summary>
/// The GraphQL surface — the "Graph" requirement of the Middle level.
/// </summary>
/// <remarks>
/// <para>
/// GraphQL earns its place here rather than being bolted on for the checklist. A comment thread is
/// a tree of unknown depth, which is precisely the case REST handles badly: either the server picks
/// a depth and is wrong for someone, or the client makes one request per level. A GraphQL client
/// asks for exactly the depth it will render, in one round trip.
/// </para>
/// <para>
/// Nested replies are resolved through a <see cref="RepliesByParentDataLoader"/>, so a query five
/// levels deep costs five batched queries rather than one per node. Depth and complexity limits are
/// configured in <c>Program.cs</c> — without them, "replies { replies { replies … } }" is a
/// denial-of-service request that the schema itself invites.
/// </para>
/// </remarks>
[QueryType]
public static class CommentQueries
{
    /// <summary>Top-level comments: the same paged, sortable list the REST endpoint serves.</summary>
    public static async Task<PagedResult<CommentListItemDto>> GetComments(
        int page,
        int pageSize,
        CommentSortField sortBy,
        SortDirection direction,
        string? search,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(
            new GetTopLevelCommentsQuery(new CommentPageRequest(page, pageSize, sortBy, direction), search),
            cancellationToken);

    /// <summary>One comment, with as much of its reply tree as the query asks for.</summary>
    public static async Task<CommentNodeDto?> GetComment(
        Guid id,
        ICommentReadRepository repository,
        CancellationToken cancellationToken) =>
        await repository.GetByIdAsync(id, cancellationToken);

    /// <summary>A whole thread, eagerly nested to <paramref name="maxDepth"/>.</summary>
    public static async Task<CommentThreadDto> GetThread(
        Guid rootId,
        int maxDepth,
        ISender sender,
        CancellationToken cancellationToken) =>
        await sender.Send(new GetCommentThreadQuery(rootId, maxDepth), cancellationToken);
}

/// <summary>Resolves the <c>replies</c> field of a comment, batched.</summary>
[ObjectType<CommentNodeDto>]
public static partial class CommentNodeTypeExtensions
{
    /// <summary>
    /// Direct replies of this comment.
    /// </summary>
    /// <remarks>
    /// Goes through the DataLoader rather than querying directly. Without it, a query over a page of
    /// 25 comments each showing replies would issue 25 separate queries — the N+1 problem GraphQL is
    /// famous for making easy to write and DataLoader exists to make easy to avoid.
    /// </remarks>
    public static async Task<IReadOnlyList<CommentNodeDto>> GetReplies(
        [Parent] CommentNodeDto comment,
        RepliesByParentDataLoader loader,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ArgumentNullException.ThrowIfNull(loader);

        // Eagerly loaded threads already carry their children; going back to the database for them
        // would be work the query has already paid for.
        if (comment.Replies.Count > 0)
        {
            return comment.Replies;
        }

        return await loader.LoadAsync(comment.Id, cancellationToken) ?? [];
    }
}

/// <summary>Batches "give me the replies of these parents" into one query per level.</summary>
public sealed class RepliesByParentDataLoader(
    ICommentReadRepository repository,
    IBatchScheduler batchScheduler,
    DataLoaderOptions options) : BatchDataLoader<Guid, IReadOnlyList<CommentNodeDto>>(batchScheduler, options)
{
    protected override async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CommentNodeDto>>> LoadBatchAsync(
        IReadOnlyList<Guid> keys,
        CancellationToken cancellationToken) =>
        await repository.GetRepliesByParentIdsAsync(keys, cancellationToken);
}
