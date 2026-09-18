using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Models;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// Read-side access to SQL. Separate from <see cref="ICommentRepository"/> because it returns DTOs
/// with no change tracking, and because for the top-level list it is the <em>fallback</em> path:
/// the table is normally served by Elasticsearch, and this is what answers when search is down.
/// </summary>
public interface ICommentReadRepository
{
    Task<PagedResult<CommentListItemDto>> GetTopLevelAsync(
        CommentPageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of a thread, depth-first, read as a single range scan over <c>(RootId, Path)</c>.
    /// Returns <see langword="null"/> when the thread does not exist.
    /// </summary>
    Task<CommentThreadDto?> GetThreadAsync(
        Guid rootId,
        int maxDepth,
        int limit,
        string? afterPath,
        CancellationToken cancellationToken = default);

    /// <summary>Batched lookup used by the GraphQL DataLoader to avoid N+1 on nested replies.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CommentNodeDto>>> GetRepliesByParentIdsAsync(
        IReadOnlyList<Guid> parentIds,
        CancellationToken cancellationToken = default);

    Task<CommentNodeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
