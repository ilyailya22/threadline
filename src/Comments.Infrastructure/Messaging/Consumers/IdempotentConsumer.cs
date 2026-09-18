using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Persistence.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Messaging.Consumers;

/// <summary>
/// Base class that makes a consumer safe to run against at-least-once delivery.
/// </summary>
/// <remarks>
/// <para>
/// The outbox guarantees every event is published at least once; a broker redelivery, a worker that
/// died between doing the work and acking, or a retry after a transient fault all mean a consumer
/// will occasionally see the same message twice. Handling that is not optional — a duplicated
/// "increment the reply count" is a visibly wrong number on the page.
/// </para>
/// <para>
/// The inbox row is inserted in the <em>same transaction</em> as the side effects that can
/// participate in one. Where a side effect is external (Elasticsearch, Blob Storage) the ordering is
/// "do the work, then record it", which can duplicate work after a crash but never skips it — and
/// those operations are written to be idempotent in their own right: indexing is by document id and
/// blobs are written to deterministic paths, so doing either twice produces the same state.
/// </para>
/// </remarks>
public abstract partial class IdempotentConsumer<TMessage>(
    AppDbContext dbContext,
    IDateTimeProvider clock,
    ILogger logger) : IConsumer<TMessage>
    where TMessage : class
{
    protected AppDbContext Context { get; } = dbContext;

    protected IDateTimeProvider Clock { get; } = clock;

    public async Task Consume(ConsumeContext<TMessage> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messageId = context.MessageId
            ?? throw new InvalidOperationException(
                $"{GetType().Name} received a message without a MessageId; idempotency cannot be guaranteed.");

        var consumerType = GetType().Name;
        var cancellationToken = context.CancellationToken;

        if (await WasHandledAsync(messageId, consumerType, cancellationToken))
        {
            LogAlreadyHandled(logger, consumerType, messageId);
            return;
        }

        await HandleAsync(context.Message, cancellationToken);

        Context.InboxMessages.Add(new InboxMessage
        {
            MessageId = messageId,
            ConsumerType = consumerType,
            ProcessedAt = Clock.UtcNow,
        });

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two instances picked the same redelivery up at once and the other won the
            // primary-key race. The work is done either way, so confirm that and treat it as
            // success rather than letting the message go round again.
            Context.ChangeTracker.Clear();

            if (!await WasHandledAsync(messageId, consumerType, cancellationToken))
            {
                throw;
            }

            LogRaceLost(logger, consumerType, messageId);
        }
    }

    protected abstract Task HandleAsync(TMessage message, CancellationToken cancellationToken);

    private Task<bool> WasHandledAsync(
        Guid messageId,
        string consumerType,
        CancellationToken cancellationToken) =>
        Context.InboxMessages
            .AsNoTracking()
            .AnyAsync(m => m.MessageId == messageId && m.ConsumerType == consumerType, cancellationToken);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping {Consumer}: message {MessageId} was already handled")]
    private static partial void LogAlreadyHandled(ILogger logger, string consumer, Guid messageId);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "{Consumer} lost the idempotency race for message {MessageId}; treating as handled")]
    private static partial void LogRaceLost(ILogger logger, string consumer, Guid messageId);
}
