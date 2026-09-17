namespace Threadline.Comments.Infrastructure.Messaging.Contracts;

/// <summary>
/// Contracts carried over RabbitMQ.
/// </summary>
/// <remarks>
/// They are separate types from the domain events on purpose. A domain event is an internal detail
/// that may be refactored freely; an integration event is a published contract that other
/// deployables — possibly older versions of this same worker during a rolling deploy — must keep
/// being able to read. Keeping them apart is what makes the domain refactorable.
/// </remarks>
public sealed record CommentCreatedIntegrationEvent
{
    public required Guid EventId { get; init; }

    public required Guid CommentId { get; init; }

    public required Guid RootId { get; init; }

    public Guid? ParentId { get; init; }

    public required Guid AuthorId { get; init; }

    public required int Depth { get; init; }

    public IReadOnlyList<Guid> AttachmentIds { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public bool IsTopLevel => ParentId is null;
}

/// <summary>
/// Raised by the worker once an attachment has been downscaled and is servable.
/// </summary>
/// <remarks>
/// This exists so the worker never needs to know that SignalR is how the news reaches a browser.
/// The worker finishes a file and says so; the API, which owns the hub connections, listens and
/// pushes. Losing one of these messages costs a user one manual refresh, which is why it is
/// published directly rather than through the outbox — the write it reports has already been
/// committed by the same consumer's transaction.
/// </remarks>
public sealed record AttachmentReadyIntegrationEvent
{
    public required Guid CommentId { get; init; }

    public required Guid AttachmentId { get; init; }
}
