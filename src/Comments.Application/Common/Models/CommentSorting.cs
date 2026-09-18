namespace Threadline.Comments.Application.Common.Models;

/// <summary>
/// Fields the top-level comment table can be sorted by. The assignment names exactly these three.
/// </summary>
/// <remarks>
/// Sorting is an enum, never a client-supplied column name. That is the structural defence against
/// SQL injection through an <c>ORDER BY</c> clause: there is no code path where user text becomes
/// part of a query, so there is nothing to escape and nothing to forget to escape.
/// </remarks>
public enum CommentSortField
{
    /// <summary>Date added — the default.</summary>
    CreatedAt = 0,
    UserName = 1,
    Email = 2,
}

public enum SortDirection
{
    /// <summary>Newest first. The assignment's default: "Сортировка по умолчанию – LIFO".</summary>
    Descending = 0,
    Ascending = 1,
}

/// <summary>Normalised, always-valid sort + page request for the top-level table.</summary>
public sealed record CommentPageRequest(
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    CommentSortField SortBy = CommentSortField.CreatedAt,
    SortDirection Direction = SortDirection.Descending)
{
    /// <summary>
    /// Clamps anything a client can send. Called at the edge so that handlers, the SQL repository
    /// and the Elasticsearch adapter all agree on the same bounds.
    /// </summary>
    public CommentPageRequest Normalized() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize switch
        {
            < 1 => Paging.DefaultPageSize,
            > Paging.MaxPageSize => Paging.MaxPageSize,
            _ => PageSize,
        },
    };

    public int Skip => (Page - 1) * PageSize;

    /// <summary>Cache key fragment — stable, short, and free of user-controlled text.</summary>
    public string ToCacheKey() =>
        $"top:{(int)SortBy}:{(int)Direction}:{Page}:{PageSize}";
}
