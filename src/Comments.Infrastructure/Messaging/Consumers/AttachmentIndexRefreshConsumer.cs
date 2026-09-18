using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Search;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Messaging.Consumers;

/// <summary>
/// Refreshes a thread's search document once its attachment has been processed.
/// </summary>
/// <remarks>
/// The document is written when the comment is created, while the image is still being downscaled,
/// so it records the attachment as pending. Without this the table would show "обрабатывается" for
/// that image indefinitely, even after the thread view shows it ready.
/// </remarks>
public sealed class AttachmentIndexRefreshConsumer(
    AppDbContext context,
    IDateTimeProvider clock,
    CommentSearchProjector projector,
    ICommentCache cache,
    ILogger<AttachmentIndexRefreshConsumer> logger)
    : IdempotentConsumer<AttachmentReadyIntegrationEvent>(context, clock, logger)
{
    protected override async Task HandleAsync(
        AttachmentReadyIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // A no-op for replies: the projector ignores anything that is not a thread root.
        await projector.ProjectAsync(message.CommentId, cancellationToken);
        await cache.InvalidateTopLevelAsync(cancellationToken);
    }
}
