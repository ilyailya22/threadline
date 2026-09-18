using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Search;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Messaging.Consumers;

/// <summary>
/// Keeps the Elasticsearch read model and the list cache in step with SQL.
/// </summary>
/// <remarks>
/// <para>
/// Replies are not indexed themselves — the table only shows top-level entries — but they change
/// their thread's reply count, so every event, root or reply, re-projects the thread root from SQL.
/// That is what keeps the write path a single insert: the cost of "how many replies does this
/// thread have" is paid here, asynchronously, instead of on every read or every write.
/// </para>
/// <para>
/// Re-projecting rather than incrementing is deliberate. An increment is only correct if it is
/// applied exactly once and after the root exists; with at-least-once delivery and reordered
/// messages, neither holds — a reply saved before its root was indexed gets counted by the root's
/// projection <em>and</em> by its own increment. A projection is simply correct whenever it runs,
/// and the index's external versioning discards any that finish out of order.
/// </para>
/// </remarks>
public sealed class CommentIndexerConsumer(
    AppDbContext context,
    IDateTimeProvider clock,
    CommentSearchProjector projector,
    ICommentCache cache,
    ILogger<CommentIndexerConsumer> logger)
    : IdempotentConsumer<CommentCreatedIntegrationEvent>(context, clock, logger)
{
    protected override async Task HandleAsync(
        CommentCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await projector.ProjectAsync(message.RootId, cancellationToken);

        // Both cases change what the table shows — a new row, or a changed reply count on an
        // existing one — so the cached pages are dropped either way.
        await cache.InvalidateTopLevelAsync(cancellationToken);
    }
}

/// <summary>
/// Refreshes a thread's search document once its attachment has been processed.
/// </summary>
/// <remarks>
/// The document is written when the comment is created, while the image is still being downscaled,
/// so it records the attachment as pending. Without this consumer the table would show "обрабатывается"
/// for that image forever, even after the thread view shows it ready.
/// </remarks>
public sealed class AttachmentIndexRefreshConsumer(
    AppDbContext context,
    IDateTimeProvider clock,
    CommentSearchProjector projector,
    ICommentCache cache,
    ILogger<AttachmentIndexRefreshConsumer> logger)
    : IdempotentConsumer<AttachmentReadyIntegrationEvent>(context, clock, logger)
{
    protected override async Task HandleAsync(
        AttachmentReadyIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // A no-op for replies: the projector ignores anything that is not a thread root.
        await projector.ProjectAsync(message.CommentId, cancellationToken);
        await cache.InvalidateTopLevelAsync(cancellationToken);
    }
}
