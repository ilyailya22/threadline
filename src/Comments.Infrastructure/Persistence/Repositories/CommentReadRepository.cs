using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL read model. Every query here is <c>AsNoTracking</c> and projects straight into DTOs, so the
/// change tracker never sees a read.
/// </summary>
/// <remarks>
/// This type answers two different needs. Thread retrieval is its <em>primary</em> job and is fast
/// by design — one range scan over <c>IX_Comments_RootId_Path</c>. The top-level list, by contrast,
/// is the <em>fallback</em> for when Elasticsearch is unavailable; sorting a million-row table by
/// the author's e-mail means a join and a sort that the search index does for free, and that cost
/// is accepted deliberately as the price of staying up rather than returning an error.
/// </remarks>
public sealed class CommentReadRepository(AppDbContext context, IAttachmentUrlBuilder urls)
    : ICommentReadRepository
{
    public async Task<PagedResult<CommentListItemDto>> GetTopLevelAsync(
        CommentPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Normalized();

        var query = context.Comments.AsNoTracking().Where(c => c.ParentId == null);

        var total = await query.LongCountAsync(cancellationToken);

        if (total == 0)
        {
            return new PagedResult<CommentListItemDto>([], page.Page, page.PageSize, 0);
        }

        var rows = await ApplySort(query, page)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(c => new Row(
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
            .AsNoTracking()
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
                Preview(r.TextPlain),
                r.CreatedAt,
                stats.Item1,
                stats.Item1 == 0 ? null : stats.Item2,
                attachments.TryGetValue(r.Id, out var files) ? files : []);
        });

        return new PagedResult<CommentListItemDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<CommentThreadDto?> GetThreadAsync(
        Guid rootId,
        int maxDepth,
        CancellationToken cancellationToken = default)
    {
        // One indexed range scan returns the whole subtree, already ordered depth-first because
        // the materialised path sorts that way. The nesting is then assembled in memory in O(n).
        var rows = await context.Comments
            .AsNoTracking()
            .Where(c => c.RootId == rootId && c.Depth <= maxDepth)
            .OrderBy(c => c.Path)
            .Select(c => new NodeRow(
                c.Id,
                c.ParentId,
                c.RootId,
                c.Depth,
                c.AuthorId,
                c.Author.UserName.Value,
                c.Author.Email.Value,
                c.Author.HomePage == null ? null : c.Author.HomePage.Value,
                c.Body.Html,
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var attachments = await LoadAttachmentsAsync(rows.ConvertAll(r => r.Id), cancellationToken);

        var root = BuildTree(rows, attachments, rootId);

        return root is null ? null : new CommentThreadDto(root, rows.Count);
    }

    public async Task<IReadOnlyList<CommentNodeDto>> GetRepliesAsync(
        Guid parentId,
        CancellationToken cancellationToken = default)
    {
        var byParent = await GetRepliesByParentIdsAsync([parentId], cancellationToken);

        return byParent.TryGetValue(parentId, out var replies) ? replies : [];
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

        var rows = await context.Comments
            .AsNoTracking()
            .Where(c => c.ParentId != null && parentIds.Contains(c.ParentId.Value))
            .OrderBy(c => c.Path)
            .Select(c => new NodeRow(
                c.Id,
                c.ParentId,
                c.RootId,
                c.Depth,
                c.AuthorId,
                c.Author.UserName.Value,
                c.Author.Email.Value,
                c.Author.HomePage == null ? null : c.Author.HomePage.Value,
                c.Body.Html,
                c.CreatedAt))
            .ToListAsync(cancellationToken);

        var attachments = await LoadAttachmentsAsync(rows.ConvertAll(r => r.Id), cancellationToken);

        return rows
            .GroupBy(r => r.ParentId!.Value)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<CommentNodeDto> (g) => [.. g.Select(r => ToNode(r, attachments))]);
    }

    public async Task<CommentNodeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Comments
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new NodeRow(
                c.Id,
                c.ParentId,
                c.RootId,
                c.Depth,
                c.AuthorId,
                c.Author.UserName.Value,
                c.Author.Email.Value,
                c.Author.HomePage == null ? null : c.Author.HomePage.Value,
                c.Body.Html,
                c.CreatedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var attachments = await LoadAttachmentsAsync([id], cancellationToken);

        return ToNode(row, attachments);
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

    private async Task<Dictionary<Guid, IReadOnlyList<AttachmentDto>>> LoadAttachmentsAsync(
        List<Guid> commentIds,
        CancellationToken cancellationToken)
    {
        if (commentIds.Count == 0)
        {
            return [];
        }

        var attachments = await context.Attachments
            .AsNoTracking()
            .Where(a => commentIds.Contains(a.CommentId))
            .ToListAsync(cancellationToken);

        return attachments
            .GroupBy(a => a.CommentId)
            .ToDictionary(
                g => g.Key,
                IReadOnlyList<AttachmentDto> (g) => [.. g.Select(urls.ToDto)]);
    }

    private static CommentNodeDto ToNode(
        NodeRow row,
        IReadOnlyDictionary<Guid, IReadOnlyList<AttachmentDto>> attachments) =>
        new(
            row.Id,
            row.ParentId,
            row.RootId,
            row.Depth,
            new AuthorDto(row.AuthorId, row.UserName, row.Email, row.HomePage),
            row.TextHtml,
            row.CreatedAt,
            attachments.TryGetValue(row.Id, out var files) ? files : []);

    private CommentNodeDto? BuildTree(
        List<NodeRow> rows,
        IReadOnlyDictionary<Guid, IReadOnlyList<AttachmentDto>> attachments,
        Guid rootId)
    {
        var childrenByParent = new Dictionary<Guid, List<NodeRow>>();

        foreach (var row in rows)
        {
            if (row.ParentId is { } parentId)
            {
                if (!childrenByParent.TryGetValue(parentId, out var siblings))
                {
                    siblings = [];
                    childrenByParent[parentId] = siblings;
                }

                siblings.Add(row);
            }
        }

        var rootRow = rows.Find(r => r.Id == rootId);

        return rootRow is null ? null : Build(rootRow);

        CommentNodeDto Build(NodeRow row)
        {
            var node = ToNode(row, attachments);

            return childrenByParent.TryGetValue(row.Id, out var children)
                ? node with { Replies = children.ConvertAll(Build) }
                : node;
        }
    }

    private static string Preview(string plainText) =>
        plainText.Length <= 200 ? plainText : plainText[..200] + "…";

    private sealed record Row(
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
        DateTimeOffset CreatedAt);
}
