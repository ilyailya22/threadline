namespace Threadline.Comments.Application.Common.Models;

/// <summary>Paging limits shared by every read path, so client, SQL and Elasticsearch agree.</summary>
public static class Paging
{
    /// <summary>The assignment fixes the page size of the top-level table at 25.</summary>
    public const int DefaultPageSize = 25;

    public const int MaxPageSize = 100;

    /// <summary>
    /// Hard ceiling on deep paging. Past this, offset paging degrades on any engine (Elasticsearch
    /// refuses beyond <c>index.max_result_window</c> outright), so the API returns a clear error and
    /// points at keyset paging instead of timing out.
    /// </summary>
    public const int MaxOffset = 10_000;
}

/// <summary>One page of results plus everything the UI needs to render a pager.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount)
{
    /// <summary>
    /// Pages the client may navigate to. The count of items is exact, but deep offset paging is
    /// capped at <see cref="Paging.MaxOffset"/>, so the pager stops there even when there is more.
    /// </summary>
    public int TotalPages => PageSize <= 0
        ? 0
        : (int)Math.Min(
            Math.Ceiling(TotalCount / (double)PageSize),
            Math.Ceiling(Paging.MaxOffset / (double)PageSize));

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;
}
