using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Comments.Events;

/// <summary>
/// Raised when a comment has been accepted. Everything that is not needed to answer the HTTP
/// request — search indexing, thumbnail generation, live fan-out to connected browsers, cache
/// invalidation — hangs off this event and runs on a worker.
/// </summary>
public sealed record CommentCreatedDomainEvent(
    Guid CommentId,
    Guid RootId,
    Guid? ParentId,
    Guid UserId,
    int Depth,
    IReadOnlyCollection<Guid> AttachmentIds,
    DateTimeOffset CreatedAt) : IDomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; } = CreatedAt;
}
