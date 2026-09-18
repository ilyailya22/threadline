namespace Threadline.Comments.Infrastructure.Persistence.Outbox;

/// <summary>
/// Record of a message a given consumer has already handled.
/// </summary>
/// <remarks>
/// At-least-once delivery means a consumer will occasionally see the same message twice — a broker
/// redelivery, a worker that crashed after doing the work but before acking. The (MessageId,
/// ConsumerType) primary key turns "handle this message" into an insert that either succeeds once
/// or violates a unique constraint, which is the cheapest correct idempotency check there is.
/// </remarks>
public sealed class InboxMessage
{
    public required Guid MessageId { get; init; }

    public required string ConsumerType { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
