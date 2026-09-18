using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Models;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// Caches pages of the top-level comment list.
/// </summary>
/// <remarks>
/// The interface is get-or-create rather than get/set on purpose. Under the traffic this system is
/// sized for, a plain get/set has a cache-stampede failure mode: the moment a hot key expires, every
/// concurrent request misses at once and they all run the same expensive query. Handing the factory
/// to the cache lets the implementation collapse those into one.
/// </remarks>
public interface ICommentCache
{
    Task<PagedResult<CommentListItemDto>> GetOrCreateTopLevelAsync(
        string key,
        Func<CancellationToken, ValueTask<PagedResult<CommentListItemDto>>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every cached list page. Called on each new top-level comment: with LIFO ordering the
    /// first page changes on every insert anyway, so per-key invalidation would buy nothing and a
    /// tag-based flush keeps the cache honest.
    /// </summary>
    Task InvalidateTopLevelAsync(CancellationToken cancellationToken = default);
}
