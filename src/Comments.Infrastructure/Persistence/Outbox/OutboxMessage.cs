using System.Text.Json;

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
/// <see cref="InboxMessage"/>. The retry policy lives here, on the message, so the publisher only
/// reports what happened and does not decide what it means.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    public const int MaxErrorLength = 2000;

    /// <summary>Retries back off exponentially up to this ceiling.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private OutboxMessage()
    {
        // EF Core
    }

    public Guid Id { get; private init; }

    /// <summary>Contract name of the integration event, e.g. <c>CommentCreatedIntegrationEvent</c>.</summary>
    public string Type { get; private init; } = null!;

    /// <summary>JSON body of the integration event.</summary>
    public string Payload { get; private init; } = null!;

    public DateTimeOffset OccurredAt { get; private init; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    /// <summary>When the publisher may try again after a transient failure.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Wraps an integration event for publishing once the surrounding transaction commits.</summary>
    public static OutboxMessage For(Guid id, object integrationEvent, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        return new OutboxMessage
        {
            Id = id,
            Type = integrationEvent.GetType().Name,
            Payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Json),
            OccurredAt = occurredAt,
        };
    }

    /// <summary>Reads the integration event back as the given contract type.</summary>
    public object ReadPayload(Type contract) =>
        JsonSerializer.Deserialize(Payload, contract, Json)
        ?? throw new InvalidOperationException($"Outbox message {Id} has an empty payload.");

    public void MarkPublished(DateTimeOffset now)
    {
        ProcessedAt = now;
        Error = null;
    }

    /// <summary>Records a failed attempt and schedules the next one: 1 s, 2 s, 4 s … up to five minutes.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(error);

        Attempts++;
        Error = error[..Math.Min(error.Length, MaxErrorLength)];
        NextAttemptAt = now.Add(Backoff(Attempts));
    }

    private static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, Math.Pow(2, Math.Min(attempts - 1, 16))));
}
