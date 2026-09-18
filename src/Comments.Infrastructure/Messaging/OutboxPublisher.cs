using System.Text.Json;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Persistence.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Threadline.Comments.Infrastructure.Messaging;

/// <summary>
/// Moves committed outbox rows onto RabbitMQ.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it polls instead of being pushed to.</b> The whole point of the outbox is that the
/// producer never talks to the broker inside its transaction. Something has to read the table
/// afterwards, and polling every half second is the simplest thing that cannot lose a message.
/// When the table is busy the loop does not sleep at all, so latency under load is a batch, not a
/// poll interval.
/// </para>
/// <para>
/// <b>Why it is safe to run N of these.</b> The claim query takes <c>UPDLOCK, READPAST</c>: each
/// instance locks the rows it takes and skips rows another instance has already locked, instead of
/// blocking on them. Scaling the worker out therefore scales publishing out, with no leader
/// election and no distributed lock.
/// </para>
/// <para>
/// <b>Failure handling.</b> A message that fails is not retried in a tight loop — it gets an
/// exponential backoff written to <c>NextAttemptAt</c>, so one poison message cannot starve the
/// queue behind it. After <see cref="OutboxOptions.MaxAttempts"/> it is parked with its error,
/// visible to the health check, rather than retried forever.
/// </para>
/// </remarks>
public sealed partial class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly OutboxOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            int published;

            try
            {
                published = await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogBatchFailed(logger, exception);
                published = 0;
            }

            if (published == 0)
            {
                await Task.Delay(_options.PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        // The context uses a retrying execution strategy, which refuses a user-opened transaction
        // unless the whole unit is handed to it — so it can replay the unit, not half of it, after a
        // transient fault. A replay may publish a message twice; consumers are idempotent for that.
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            ct => ClaimAndPublishAsync(context, bus, clock.UtcNow, ct),
            cancellationToken);
    }

    private async Task<int> ClaimAndPublishAsync(
        AppDbContext context,
        IPublishEndpoint bus,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        // READPAST lets sibling publishers work on disjoint rows instead of queueing behind each
        // other; UPDLOCK holds what this instance took until the transaction commits.
        //
        // AsTracking is essential: the context defaults to no-tracking for reads, and an untracked
        // batch would make the SaveChanges below a no-op — every message would be published again
        // on every pass, forever.
        var batch = await context.OutboxMessages
            .FromSql(
                $"""
                 SELECT TOP ({_options.BatchSize}) *
                 FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                 WHERE [ProcessedAt] IS NULL
                   AND ([NextAttemptAt] IS NULL OR [NextAttemptAt] <= {now})
                   AND [Attempts] < {_options.MaxAttempts}
                 ORDER BY [OccurredAt]
                 """)
            .AsTracking()
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        foreach (var message in batch)
        {
            try
            {
                await PublishAsync(bus, message, cancellationToken);

                message.ProcessedAt = now;
                message.Error = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.Attempts++;
                message.Error = exception.Message[..Math.Min(exception.Message.Length, 2000)];
                message.NextAttemptAt = now.Add(Backoff(message.Attempts));

                LogPublishFailed(logger, message.Id, message.Type, message.Attempts, exception);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var published = batch.Count(m => m.ProcessedAt is not null);

        LogBatchPublished(logger, published, batch.Count);

        return published;
    }

    private static Task PublishAsync(
        IPublishEndpoint bus,
        OutboxMessage message,
        CancellationToken cancellationToken) =>
        message.Type switch
        {
            nameof(CommentCreatedIntegrationEvent) => bus.Publish(
                Deserialize<CommentCreatedIntegrationEvent>(message),
                context => context.MessageId = message.Id,
                cancellationToken),
            _ => throw new NotSupportedException($"Unknown outbox message type '{message.Type}'."),
        };

    private static T Deserialize<T>(OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(message.Payload, Json)
        ?? throw new InvalidOperationException($"Outbox message {message.Id} has an empty payload.");

    /// <summary>Exponential backoff capped at five minutes: 1s, 2s, 4s … 300s.</summary>
    private static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempts, 9))));

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox publisher started (batch size {BatchSize})")]
    private static partial void LogStarted(ILogger logger, int batchSize);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Published {Published} of {Total} outbox messages")]
    private static partial void LogBatchPublished(ILogger logger, int published, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox batch failed")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to publish outbox message {MessageId} ({MessageType}), attempt {Attempts}")]
    private static partial void LogPublishFailed(
        ILogger logger,
        Guid messageId,
        string messageType,
        int attempts,
        Exception exception);
}
