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
