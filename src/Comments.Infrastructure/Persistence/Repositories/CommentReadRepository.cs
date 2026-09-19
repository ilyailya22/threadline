using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL read model. Queries run untracked (the context default) and project straight into DTOs, so
/// the change tracker never sees a read.
/// </summary>
/// <remarks>
/// This type answers two different needs. Thread retrieval is its <em>primary</em> job and is fast
/// by design — one range scan over <c>IX_Comments_RootId_Path</c>. The top-level list, by contrast,
/// is the <em>fallback</em> for when Elasticsearch is unavailable; sorting a million-row table by
/// the author's e-mail means a join and a sort that the search index does for free, and that cost
/// is accepted deliberately as the price of staying up rather than returning an error.
/// </remarks>
public sealed class CommentReadRepository(AppDbContext context, IAttachmentDtoMapper attachmentMapper)
    : ICommentReadRepository
{
    public async Task<PagedResult<CommentListItemDto>> GetTopLevelAsync(
        CommentPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Normalized();

        var topLevel = context.Comments.Where(c => c.ParentId == null);

        var total = await topLevel.LongCountAsync(cancellationToken);

        if (total == 0)
        {
            return new PagedResult<CommentListItemDto>([], page.Page, page.PageSize, 0);
        }

        var rows = await ApplySort(topLevel, page)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(c => new ListItemRow(
                c.Id,
                c.AuthorId,
                c.Author.UserName.Value,
                c.Author.Email.Value,
                c.Author.HomePage == null ? null : c.Author.HomePage.Value,
                c.Body.Html,
                c.Body.PlainText,
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        var ids = rows.ConvertAll(r => r.Id);

        var replyStats = await context.Comments
            .Where(c => ids.Contains(c.RootId) && c.ParentId != null)
            .GroupBy(c => c.RootId)
            .Select(g => new { RootId = g.Key, Count = g.Count(), Last = g.Max(c => c.CreatedAt) })
            .ToDictionaryAsync(x => x.RootId, x => (x.Count, x.Last), cancellationToken);

        var attachments = await LoadAttachmentsAsync(ids, cancellationToken);

        var items = rows.ConvertAll(r =>
        {
            replyStats.TryGetValue(r.Id, out var stats);

            return new CommentListItemDto(
                r.Id,
                new AuthorDto(r.AuthorId, r.UserName, r.Email, r.HomePage),
                r.TextHtml,
                CommentBody.ToPreview(r.TextPlain),
                r.CreatedAt,
                stats.Count,
                stats.Count == 0 ? null : stats.Last,
                AttachmentsOf(r.Id, attachments));
        });

        return new PagedResult<CommentListItemDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<CommentThreadDto?> GetThreadAsync(
        Guid rootId,
        int maxDepth,
        int limit,
        ThreadCursor? after,
        CancellationToken cancellationToken = default)
    {
        var thread = context.Comments.Where(c => c.RootId == rootId && c.Depth <= maxDepth);

        // Keyset on (path, id): "everything after the last node you saw", served by the same
        // (RootId, Path) index range scan as the first page — the clustered Id rides along in every
        // index, so the tie-breaker needs no extra sort. See ThreadCursor for why the path alone is
        // not enough. Offset paging would make page 100
        // of a large thread scan and discard 99 pages of rows; this costs the same on every page.
        //
        // The comparison is written as parameterised SQL rather than LINQ: Path is a value object with
        // a converter, and EF cannot translate an ordering comparison on it. FromSql composes with the
        // LINQ below, and every value is a parameter, never text. The collation orders the path's hex
        // characters exactly as ordinal comparison does, which keeps pages in depth-first order.
        var page = after is null
            ? thread
            : context.Comments
                .FromSql(
                    $"""
                     SELECT * FROM [Comments]
                     WHERE [RootId] = {rootId} AND [Depth] <= {maxDepth}
                       AND ([Path] > {after.Path} OR ([Path] = {after.Path} AND [Id] > {after.Id}))
                     """);

        // One row more than asked for tells us whether there is a next page without a second query.
        var rows = await ProjectToNodeRows(page.OrderBy(c => c.Path).ThenBy(c => c.Id).Take(limit + 1))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0 && after is null)
        {
            return null;
        }

        var hasMore = rows.Count > limit;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var total = await thread.CountAsync(cancellationToken);
        var nodes = await ToNodesAsync(rows, cancellationToken);

        var next = hasMore ? new ThreadCursor(rows[^1].Path, rows[^1].Id).ToString() : null;

        return new CommentThreadDto(rootId, total, nodes, next);
    }

    /// <summary>
    /// Batched sibling lookup. This is what the GraphQL DataLoader calls: one query for every parent
    /// on a level instead of one query per parent, which is the difference between a deep thread
    /// costing N queries and costing one per level.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CommentNodeDto>>> GetRepliesByParentIdsAsync(
        IReadOnlyList<Guid> parentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parentIds);

        if (parentIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<CommentNodeDto>>();
        }

        var replies = context.Comments
            .Where(c => c.ParentId != null && parentIds.Contains(c.ParentId.Value))
            .OrderBy(c => c.Path)
            .ThenBy(c => c.Id);

        var nodes = await ToNodesAsync(
            await ProjectToNodeRows(replies).ToListAsync(cancellationToken),
            cancellationToken);

        return nodes
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, IReadOnlyList<CommentNodeDto> (g) => [.. g]);
    }

    public async Task<CommentNodeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await ProjectToNodeRows(context.Comments.Where(c => c.Id == id))
            .ToListAsync(cancellationToken);

        var nodes = await ToNodesAsync(rows, cancellationToken);

        return nodes.Count == 0 ? null : nodes[0];
    }

    /// <summary>
    /// Sorting is driven by an enum, so the set of possible <c>ORDER BY</c> clauses is closed and
    /// known at compile time. No part of it can come from the request body.
    /// </summary>
    private static IQueryable<Comment> ApplySort(IQueryable<Comment> query, CommentPageRequest page) =>
        (page.SortBy, page.Direction) switch
        {
            (CommentSortField.UserName, SortDirection.Ascending) =>
                query.OrderBy(c => c.Author.UserName.Value).ThenByDescending(c => c.Id),
            (CommentSortField.UserName, _) =>
                query.OrderByDescending(c => c.Author.UserName.Value).ThenByDescending(c => c.Id),
            (CommentSortField.Email, SortDirection.Ascending) =>
                query.OrderBy(c => c.Author.Email.Value).ThenByDescending(c => c.Id),
            (CommentSortField.Email, _) =>
                query.OrderByDescending(c => c.Author.Email.Value).ThenByDescending(c => c.Id),
            (_, SortDirection.Ascending) => query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),

            // The default: LIFO. Id is UUID v7, so ordering by it is ordering by creation time and
            // also breaks ties deterministically — without it, two comments in the same millisecond
            // could swap places between pages and the user would see a duplicate or a gap.
            _ => query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id),
        };

    /// <summary>The one projection every node-shaped read goes through, so they cannot drift apart.</summary>
    private static IQueryable<NodeRow> ProjectToNodeRows(IQueryable<Comment> comments) =>
        comments.Select(c => new NodeRow(
            c.Id,
            c.ParentId,
            c.RootId,
            c.Depth,
            c.AuthorId,
            c.Author.UserName.Value,
            c.Author.Email.Value,
            c.Author.HomePage == null ? null : c.Author.HomePage.Value,
            c.Body.Html,
            c.CreatedAt,
            c.Path.Value));

    private async Task<List<CommentNodeDto>> ToNodesAsync(List<NodeRow> rows, CancellationToken cancellationToken)
    {
        var attachments = await LoadAttachmentsAsync(rows.ConvertAll(r => r.Id), cancellationToken);

        return rows.ConvertAll(row => new CommentNodeDto(
            row.Id,
            row.ParentId,
            row.RootId,
            row.Depth,
            new AuthorDto(row.AuthorId, row.UserName, row.Email, row.HomePage),
            row.TextHtml,
            row.CreatedAt,
            AttachmentsOf(row.Id, attachments)));
    }

    private async Task<Dictionary<Guid, IReadOnlyList<AttachmentDto>>> LoadAttachmentsAsync(
        List<Guid> commentIds,
        CancellationToken cancellationToken)
    {
        if (commentIds.Count == 0)
        {
            return [];
        }

        var attachments = await context.Attachments
            .Where(a => commentIds.Contains(a.CommentId))
            .ToListAsync(cancellationToken);

        return attachments
            .GroupBy(a => a.CommentId)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<AttachmentDto> (g) => [.. g.Select(attachmentMapper.ToDto)]);
    }

    private static IReadOnlyList<AttachmentDto> AttachmentsOf(
        Guid commentId,
        Dictionary<Guid, IReadOnlyList<AttachmentDto>> attachments) =>
        attachments.TryGetValue(commentId, out var files) ? files : [];

    private sealed record ListItemRow(
        Guid Id,
        Guid AuthorId,
        string UserName,
        string Email,
        string? HomePage,
        string TextHtml,
        string TextPlain,
        DateTimeOffset CreatedAt);

    private sealed record NodeRow(
        Guid Id,
        Guid? ParentId,
        Guid RootId,
        int Depth,
        Guid AuthorId,
        string UserName,
        string Email,
        string? HomePage,
        string TextHtml,
        DateTimeOffset CreatedAt,
        string Path);
}
