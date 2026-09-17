using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Messaging.Consumers;

/// <summary>
/// Turns an accepted upload into what the page actually serves: an image downscaled to at most
/// 320×240 and a thumbnail for the lightbox.
/// </summary>
/// <remarks>
/// This is the work that must not happen on the request thread. Decoding and resampling a 10 MB
/// photograph takes tens to hundreds of milliseconds and a burst of uploads would otherwise occupy
/// the API's threads and its memory — the exact shape of failure the Middle+ scale target is about.
/// Here it is one message on a queue that can be scaled independently of the web tier.
/// </remarks>
public sealed partial class AttachmentProcessorConsumer(
    AppDbContext context,
    IDateTimeProvider clock,
    IFileStorage storage,
    IImageProcessor images,
    ICommentNotifier notifier,
    IAttachmentUrlBuilder urls,
    ILogger<AttachmentProcessorConsumer> logger)
    : IdempotentConsumer<CommentCreatedIntegrationEvent>(context, clock, logger)
{
    protected override async Task HandleAsync(
        CommentCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.AttachmentIds.Count == 0)
        {
            return;
        }

        var attachments = await Context.Attachments
            .AsTracking()
            .Where(a => a.CommentId == message.CommentId && a.Status == AttachmentStatus.Pending)
            .ToListAsync(cancellationToken);

        foreach (var attachment in attachments)
        {
            try
            {
                if (attachment.Kind == AttachmentKind.Image)
                {
                    await ProcessImageAsync(attachment, cancellationToken);
                }
                else
                {
                    // A .txt needs no processing: it was validated and stored on the way in.
                    attachment.MarkTextProcessed(Clock.UtcNow);
                }

                await notifier.AttachmentReadyAsync(
                    message.CommentId,
                    urls.ToDto(attachment),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One broken file must not fail the message and replay the whole batch forever. The
                // attachment is marked failed, the comment still renders, and the error is visible.
                LogProcessingFailed(logger, attachment.Id, exception);
                attachment.MarkFailed(exception.Message, Clock.UtcNow);
            }
        }
    }

    private async Task ProcessImageAsync(Attachment attachment, CancellationToken cancellationToken)
    {
        var original = await storage.OpenReadAsync(attachment.StoragePath, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Original blob '{attachment.StoragePath}' for attachment {attachment.Id} is missing.");

        ProcessedImage processed;

        await using (original)
        {
            processed = await images.DownscaleAsync(
                original,
                Attachment.MaxImageWidth,
                Attachment.MaxImageHeight,
                cancellationToken);
        }

        // Deterministic paths: reprocessing the same attachment overwrites the same blobs instead of
        // leaking a new pair every time a message is redelivered.
        var displayPath = $"files/{attachment.Id:N}.png";
        var thumbnailPath = $"files/{attachment.Id:N}-thumb.webp";

        using (var content = new MemoryStream(processed.Content))
        {
            await storage.SaveAsync(displayPath, content, processed.ContentType, cancellationToken);
        }

        using (var thumbnail = new MemoryStream(processed.Thumbnail))
        {
            await storage.SaveAsync(
                thumbnailPath,
                thumbnail,
                processed.ThumbnailContentType,
                cancellationToken);
        }

        var originalPath = attachment.StoragePath;

        attachment.MarkImageProcessed(
            displayPath,
            thumbnailPath,
            processed.Width,
            processed.Height,
            processed.Content.Length,
            Clock.UtcNow);

        // The upload is no longer referenced by anything. Keeping originals would be a defensible
        // choice for re-processing later; not keeping them is the cheaper one, and it is the one
        // that matches "the assignment stores a 320×240 image".
        await storage.DeleteAsync(originalPath, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process attachment {AttachmentId}")]
    private static partial void LogProcessingFailed(ILogger logger, Guid attachmentId, Exception exception);
}
