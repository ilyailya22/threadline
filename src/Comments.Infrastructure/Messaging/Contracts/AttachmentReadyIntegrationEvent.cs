namespace Threadline.Comments.Infrastructure.Messaging.Contracts;

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
