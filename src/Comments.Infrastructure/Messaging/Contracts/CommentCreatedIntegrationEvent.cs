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
