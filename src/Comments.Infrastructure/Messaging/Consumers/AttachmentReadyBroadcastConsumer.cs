using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Messaging.Consumers;

/// <summary>
/// Pushes "your picture is ready" to the browser once the worker has downscaled it.
/// </summary>
/// <remarks>
/// The comment appears the instant it is posted; its image appears a moment later, when this
/// arrives. That is what lets the upload path stay asynchronous without the user seeing a broken
/// image or having to refresh.
/// </remarks>
public sealed class AttachmentReadyBroadcastConsumer(
    AppDbContext context,
    IDateTimeProvider clock,
    IAttachmentRepository attachments,
    IAttachmentDtoMapper attachmentMapper,
    ICommentNotifier notifier,
    ILogger<AttachmentReadyBroadcastConsumer> logger)
    : IdempotentConsumer<AttachmentReadyIntegrationEvent>(context, clock, logger)
{
    protected override async Task HandleAsync(
        AttachmentReadyIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var attachment = await attachments.FindAsync(message.AttachmentId, cancellationToken);

        if (attachment is null)
        {
            return;
        }

        await notifier.AttachmentReadyAsync(message.CommentId, attachmentMapper.ToDto(attachment), cancellationToken);
    }
}
