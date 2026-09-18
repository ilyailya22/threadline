using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Application.Common.Models;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Application.Comments.Queries.GetTopLevelComments;

/// <summary>
/// Serves the paged table through a three-tier read path: cache → Elasticsearch → SQL.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cache.</b> With LIFO ordering and 100k visitors a day, the first page of the default sort is
/// requested far more often than anything else and changes only when someone posts. A short-TTL
/// entry in Redis (fronted by an in-process L1 via <c>HybridCache</c>) absorbs that traffic
/// entirely.
/// </para>
/// <para>
/// <b>Elasticsearch.</b> The normal miss path. The documents are denormalised, so sorting by e-mail
/// or user name costs the same as sorting by date, and no join is involved.
/// </para>
/// <para>
/// <b>SQL fallback.</b> If the search cluster is unavailable the site degrades instead of failing:
/// the same page is answered from SQL Server, slower but correct. A comments board that shows a 500
/// because a search node restarted would be a worse system than one that is briefly slower.
/// </para>
/// </remarks>
public sealed partial class GetTopLevelCommentsQueryHandler(
    ICommentSearchIndex searchIndex,
    ICommentReadRepository readRepository,
    ICommentCache cache,
    ILogger<GetTopLevelCommentsQueryHandler> logger)
    : IRequestHandler<GetTopLevelCommentsQuery, PagedResult<CommentListItemDto>>
{
    public async Task<PagedResult<CommentListItemDto>> Handle(
        GetTopLevelCommentsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Page.Normalized();

        // from + size, not from alone: Elasticsearch refuses any window that ends past the limit.
        if (page.Skip + page.PageSize > Paging.MaxOffset)
        {
            throw new InputValidationException(
                "page",
                $"Cannot page beyond {Paging.MaxOffset} items. Narrow the result set instead.");
        }

        var searchText = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();

        // Free-text searches are not cached: they are rare, unbounded in shape, and caching them
        // would let a client fill the cache with junk keys.
        if (searchText is not null)
        {
            return await QueryAsync(page, searchText, cancellationToken);
        }

        var cached = await cache.GetOrCreateTopLevelAsync(
            page.ToCacheKey(),
            async token =>
            {
                var fresh = await QueryAsync(page, searchText: null, token);

                return new PagedCacheEntry(fresh.Items, fresh.Page, fresh.PageSize, fresh.TotalCount);
            },
            cancellationToken);

        return new PagedResult<CommentListItemDto>(
            cached.Items,
            cached.Page,
            cached.PageSize,
            cached.TotalCount);
    }

    private async Task<PagedResult<CommentListItemDto>> QueryAsync(
        CommentPageRequest page,
        string? searchText,
        CancellationToken cancellationToken)
    {
        try
        {
            return await searchIndex.QueryTopLevelAsync(page, searchText, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSearchUnavailable(logger, exception);

            if (searchText is not null)
            {
                // Full-text search has no meaningful SQL equivalent that would still be fast at a
                // million rows; the honest answer is "search is down", not a LIKE '%…%' scan.
                throw;
            }

            return await readRepository.GetTopLevelAsync(page, cancellationToken);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Elasticsearch query failed; falling back to SQL for the top-level comment list")]
    private static partial void LogSearchUnavailable(ILogger logger, Exception exception);
}
