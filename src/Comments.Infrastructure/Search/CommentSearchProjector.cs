using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Search;

/// <summary>
/// Builds the Elasticsearch document for a top-level comment from the SQL source of truth.
/// </summary>
/// <remarks>
/// One place that knows how a thread becomes a search document, used by every path that needs to
/// (re)write one: a new thread, an attachment that finished processing, and a full reindex. The
/// document is always rebuilt from SQL rather than patched, so whichever event arrives last leaves
/// the index correct — messages can be reordered or replayed without the index drifting.
/// </remarks>
public sealed class CommentSearchProjector(
    AppDbContext context,
    ICommentSearchIndex searchIndex,
    IAttachmentUrlBuilder urls)
{
    /// <summary>Re-projects one thread root. Does nothing if the comment is missing or is a reply.</summary>
    public async Task ProjectAsync(Guid rootId, CancellationToken cancellationToken = default)
    {
        var documents = await BuildAsync([rootId], cancellationToken);

        if (documents.Count > 0)
        {
            await searchIndex.IndexManyAsync(documents, cancellationToken);
        }
    }

    /// <summary>Builds documents for a batch of roots in three queries, whatever the batch size.</summary>
    public async Task<IReadOnlyList<CommentSearchDocument>> BuildAsync(
        IReadOnlyCollection<Guid> rootIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootIds);

        if (rootIds.Count == 0)
        {
            return [];
        }

        var roots = await context.Comments
            .AsNoTracking()
            .Where(c => rootIds.Contains(c.Id) && c.ParentId == null)
            .Select(c => new
            {
                c.Id,
                c.AuthorId,
                UserName = c.Author.UserName.Value,
                Email = c.Author.Email.Value,
                HomePage = c.Author.HomePage == null ? null : c.Author.HomePage.Value,
                Html = c.Body.Html,
                Plain = c.Body.PlainText,
                c.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        if (roots.Count == 0)
        {
            return [];
        }

        var ids = roots.ConvertAll(r => r.Id);

        // Counted from SQL rather than assumed: events can arrive out of order, and a reply's
        // increment against a not-yet-indexed root is dropped. Counting here makes every projection
        // self-healing.
        var replies = await context.Comments
            .AsNoTracking()
            .Where(c => ids.Contains(c.RootId) && c.ParentId != null)
            .GroupBy(c => c.RootId)
            .Select(g => new { RootId = g.Key, Count = g.Count(), Last = g.Max(c => c.CreatedAt) })
            .ToDictionaryAsync(x => x.RootId, cancellationToken);

        var attachments = (await context.Attachments
                .AsNoTracking()
                .Where(a => ids.Contains(a.CommentId))
                .ToListAsync(cancellationToken))
            .ToLookup(a => a.CommentId);

        return roots.ConvertAll(root =>
        {
            replies.TryGetValue(root.Id, out var stats);

            return new CommentSearchDocument
            {
                Id = root.Id,
                AuthorId = root.AuthorId,
                UserName = root.UserName,
                Email = root.Email,
                HomePage = root.HomePage,
                TextHtml = root.Html,
                TextPlain = root.Plain,
                CreatedAt = root.CreatedAt,
                ReplyCount = stats?.Count ?? 0,
                LastReplyAt = stats?.Last,
                Attachments = [.. attachments[root.Id].Select(urls.ToDto)],
            };
        });
    }
}
