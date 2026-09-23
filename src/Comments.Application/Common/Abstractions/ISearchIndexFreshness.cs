namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// Answers whether the search index still reflects what SQL holds.
/// </summary>
/// <remarks>
/// <para>
/// The list is served from Elasticsearch, which a background consumer keeps up to date. A failure
/// of Elasticsearch itself is easy to notice — the query throws, and the list falls back to SQL.
/// The failure that is <em>not</em> obvious is the one in between: the cluster is healthy and
/// answers every query, but nothing is feeding it any more, because the worker is stopped, the
/// broker is unreachable, or the consumer is stuck retrying one poisoned message. The comment is in
/// the database, the page never shows it, and no exception is thrown anywhere.
/// </para>
/// <para>
/// So the question is asked directly, of the data rather than of the machinery: is the newest
/// top-level comment in SQL also in the index? That covers every way the pipeline can stall,
/// including ones not invented yet.
/// </para>
/// </remarks>
public interface ISearchIndexFreshness
{
    /// <summary>
    /// <see langword="false"/> when SQL holds a top-level comment that the index has not caught up
    /// with, and has not for longer than the grace period that ordinary lag needs.
    /// </summary>
    ValueTask<bool> IsCurrentAsync(CancellationToken cancellationToken = default);
}
