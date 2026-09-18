using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Search;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Messaging.Consumers;

/// <summary>
/// Keeps the Elasticsearch read model and the list cache in step with SQL, a batch at a time.
/// </summary>
/// <remarks>
/// <para>
/// Replies are not indexed themselves — the table only shows top-level entries — but they change
/// their thread's reply count, so every event, root or reply, re-projects its thread root from SQL.
/// That keeps the write path a single insert: the cost of "how many replies does this thread have"
/// is paid here, asynchronously.
/// </para>
/// <para>
/// <b>Why batches.</b> Each index write waits for Elasticsearch's refresh (so the cache is never
/// re-filled from a stale index), which costs up to a second. One message at a time, that capped the
/// indexer at about thirty events a second — invisible at the target write rate, but a backlog after
/// an outage took minutes to clear. A batch of up to 200 events costs one projection query set, one
/// bulk request, one refresh wait and one cache invalidation, whatever its size.
/// </para>
/// <para>
/// <b>Why no inbox.</b> Unlike the other consumers this one needs no idempotency record: it never
/// increments anything. It rebuilds each affected document from SQL, and the index's external
/// versioning rejects any projection older than what is stored. Processing a message twice, or two
/// batches out of order, converges on the same document.
/// </para>
/// </remarks>
public sealed partial class CommentIndexerConsumer(
    CommentSearchProjector projector,
    ICommentSearchIndex searchIndex,
    ICommentCache cache,
    ILogger<CommentIndexerConsumer> logger)
    : IConsumer<Batch<CommentCreatedIntegrationEvent>>
{
    public const int BatchSize = 200;

    public async Task Consume(ConsumeContext<Batch<CommentCreatedIntegrationEvent>> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Ten replies to one thread are one projection, not ten.
        var roots = context.Message
            .Select(message => message.Message.RootId)
            .Distinct()
            .ToArray();

        var documents = await projector.BuildAsync(roots, context.CancellationToken);

        await searchIndex.IndexManyAsync(documents, context.CancellationToken);

        // The documents are searchable at this point (refresh=wait_for), so no reader can re-cache a
        // page from the index as it was before this batch.
        await cache.InvalidateTopLevelAsync(context.CancellationToken);

        LogIndexed(logger, context.Message.Length, roots.Length);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Indexed {Events} events as {Threads} thread projections")]
    private static partial void LogIndexed(ILogger logger, int events, int threads);
}

/// <summary>
/// Refreshes a thread's search document once its attachment has been processed.
/// </summary>
/// <remarks>
/// The document is written when the comment is created, while the image is still being downscaled,
/// so it records the attachment as pending. Without this the table would show "обрабатывается" for
/// that image indefinitely, even after the thread view shows it ready.
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

public static class CommentIndexerRegistration
{
    /// <summary>
    /// Registers the batching indexer. A batch closes at <see cref="CommentIndexerConsumer.BatchSize"/>
    /// events or after 100 ms, whichever comes first — so a quiet system still indexes a new comment
    /// almost immediately, and a busy one amortises the refresh wait across a whole batch.
    /// </summary>
    public static IBusRegistrationConfigurator AddCommentIndexer(this IBusRegistrationConfigurator bus)
    {
        ArgumentNullException.ThrowIfNull(bus);

        bus.AddConsumer<CommentIndexerConsumer>(consumer => consumer.Options<BatchOptions>(options => options
            .SetMessageLimit(CommentIndexerConsumer.BatchSize)
            .SetTimeLimit(TimeSpan.FromMilliseconds(100))
            .SetConcurrencyLimit(2)));

        return bus;
    }
}
