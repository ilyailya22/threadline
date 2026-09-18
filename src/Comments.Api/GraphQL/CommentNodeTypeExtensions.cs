using Threadline.Comments.Application.Comments.Dtos;

namespace Threadline.Comments.Api.GraphQL;

/// <summary>Adds the <c>replies</c> field to a comment, resolved in batches.</summary>
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

        return await loader.LoadAsync(comment.Id, cancellationToken) ?? [];
    }
}
