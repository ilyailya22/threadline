using Threadline.Comments.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Hybrid;

namespace Threadline.Comments.Infrastructure.Caching;

/// <summary>
/// Two-level cache for the top-level comment list: an in-process L1 in front of Redis as L2.
/// </summary>
/// <remarks>
/// <para>
/// L1 removes the network hop for the handful of keys that carry most of the traffic — with LIFO
/// ordering, page 1 of the default sort is requested far more than everything else combined. L2
/// keeps the replicas consistent with each other and survives a restart, so a deploy does not
/// stampede the database with cold caches.
/// </para>
/// <para>
/// The TTL is short (30 s) on purpose. The list changes whenever anyone posts, and it is also
/// invalidated explicitly by tag when that happens; the TTL is the backstop for the case where an
/// invalidation message is lost, not the primary mechanism.
/// </para>
/// </remarks>
public sealed class HybridCommentCache(HybridCache cache) : ICommentCache
{
    /// <summary>Tag used to drop every cached list page at once.</summary>
    public const string TopLevelTag = "comments:top";

    private static readonly HybridCacheEntryOptions Options = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(10),
    };

    private static readonly string[] Tags = [TopLevelTag];

    public async Task<PagedCacheEntry> GetOrCreateTopLevelAsync(
        string key,
        Func<CancellationToken, ValueTask<PagedCacheEntry>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        return await cache.GetOrCreateAsync(
            key,
            factory,
            static (state, token) => state(token),
            Options,
            Tags,
            cancellationToken);
    }

    public async Task InvalidateTopLevelAsync(CancellationToken cancellationToken = default) =>
        await cache.RemoveByTagAsync(TopLevelTag, cancellationToken);
}
