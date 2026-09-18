namespace Threadline.Comments.Application.Common.Models;

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
