namespace Threadline.Comments.Application.Common.Models;

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
