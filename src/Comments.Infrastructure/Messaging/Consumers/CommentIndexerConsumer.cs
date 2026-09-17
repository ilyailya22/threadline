using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Messaging.Consumers;

/// <summary>
/// Keeps the Elasticsearch read model and the list cache in step with SQL.
/// </summary>
/// <remarks>
/// A top-level comment becomes a new document. A reply is not indexed at all — the table only ever
/// shows top-level entries — but it does bump the denormalised counter on its thread root. That
/// asymmetry is why the write path can stay a single insert: the expensive part of "how many
/// replies does this thread have" is paid once here, asynchronously, instead of on every read.
/// </remarks>
public sealed class CommentIndexerConsumer(
    AppDbContext context,
    IDateTimeProvider clock,
    ICommentSearchIndex searchIndex,
    ICommentCache cache,
    IAttachmentUrlBuilder urls,
    ILogger<CommentIndexerConsumer> logger)
    : IdempotentConsumer<CommentCreatedIntegrationEvent>(context, clock, logger)
{
    protected override async Task HandleAsync(
        CommentCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.IsTopLevel)
        {
            await IndexRootAsync(message, cancellationToken);
        }
        else
        {
            await searchIndex.IncrementReplyCountAsync(message.RootId, message.CreatedAt, cancellationToken);
        }

        // Both cases change what the table shows — a new row, or a changed reply count on an
        // existing one — so the cached pages are dropped either way.
        await cache.InvalidateTopLevelAsync(cancellationToken);
    }

    private async Task IndexRootAsync(
        CommentCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        var row = await Context.Comments
            .AsNoTracking()
            .Where(c => c.Id == message.CommentId)
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
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            // The comment was published and then removed, or the read replica has not caught up.
            // Either way there is nothing to index and nothing to retry.
            return;
        }

        var attachments = await Context.Attachments
            .AsNoTracking()
            .Where(a => a.CommentId == message.CommentId)
            .ToListAsync(cancellationToken);

        // The reply count is read from SQL rather than assumed to be zero. Messages can arrive out
        // of order — a fast reply's event can overtake its root's — and an increment against a
        // document that does not exist yet is dropped. Counting here makes the indexer self-healing
        // instead of permanently off by however many replies won that race.
        var replies = await Context.Comments
            .AsNoTracking()
            .Where(c => c.RootId == message.CommentId && c.ParentId != null)
            .GroupBy(c => c.RootId)
            .Select(g => new { Count = g.Count(), Last = (DateTimeOffset?)g.Max(c => c.CreatedAt) })
            .FirstOrDefaultAsync(cancellationToken);

        await searchIndex.IndexAsync(
            new CommentSearchDocument
            {
                Id = row.Id,
                AuthorId = row.AuthorId,
                UserName = row.UserName,
                Email = row.Email,
                HomePage = row.HomePage,
                TextHtml = row.Html,
                TextPlain = row.Plain,
                CreatedAt = row.CreatedAt,
                ReplyCount = replies?.Count ?? 0,
                LastReplyAt = replies?.Last,
                Attachments = [.. attachments.Select(urls.ToDto)],
            },
            cancellationToken);
    }
}
