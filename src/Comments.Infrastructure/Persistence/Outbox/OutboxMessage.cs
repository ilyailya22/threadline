namespace Threadline.Comments.Infrastructure.Persistence.Outbox;

/// <summary>
/// A side effect that has been promised but not yet performed.
/// </summary>
/// <remarks>
/// <para>
/// The transactional outbox is what makes "save the comment" and "publish the event" a single
/// atomic fact. Publishing to RabbitMQ inside the request would create two failure modes that are
/// impossible to reason about: a comment saved with no event (silently missing from search forever)
/// and an event published for a comment whose transaction rolled back (a ghost in the index).
/// Writing the event as a row in the same transaction removes both.
/// </para>
/// <para>
/// Delivery is therefore at-least-once, and every consumer is idempotent — see
/// <see cref="InboxMessage"/>.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    public Guid Id { get; init; }

    /// <summary>Assembly-qualified-free contract name, e.g. <c>CommentCreated</c>.</summary>
    public required string Type { get; init; }

    /// <summary>JSON body of the integration event.</summary>
    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    /// <summary>When the publisher may try again after a transient failure.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? Error { get; set; }
}

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
