namespace Threadline.Comments.Domain.Common;

/// <summary>
/// Something that has already happened inside the domain. Domain events are recorded on the
/// aggregate, converted to outbox rows in the same transaction as the state change, and only then
/// published to RabbitMQ — so a side effect can never fire for a write that was rolled back.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }

    DateTimeOffset OccurredAt { get; }
}
