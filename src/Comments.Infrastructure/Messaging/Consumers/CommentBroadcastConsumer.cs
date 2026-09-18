using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Messaging.Contracts;
using Threadline.Comments.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Messaging.Consumers;

/// <summary>
/// Pushes a newly accepted comment to every browser currently watching, over SignalR.
/// </summary>
/// <remarks>
/// <para>
/// Hosted by the <em>API</em>, not the worker, because SignalR delivery goes through hub
/// connections that live in the web tier. With the Redis backplane any API replica can reach a
/// client connected to any other, so it does not matter which replica consumes the message.
/// </para>
/// <para>
/// This is also what hides the eventual consistency of the search index from the person who posted:
/// their comment appears immediately because it was pushed here, not because the index caught up.
/// </para>
/// </remarks>
public sealed partial class CommentBroadcastConsumer(
    AppDbContext context,
    IDateTimeProvider clock,
    ICommentReadRepository comments,
    ICommentNotifier notifier,
    ILogger<CommentBroadcastConsumer> logger)
    : IdempotentConsumer<CommentCreatedIntegrationEvent>(context, clock, logger)
{
    protected override async Task HandleAsync(
        CommentCreatedIntegrationEvent message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var comment = await comments.GetByIdAsync(message.CommentId, cancellationToken);

        if (comment is null)
        {
            LogMissing(logger, message.CommentId);
            return;
        }

        await notifier.CommentCreatedAsync(comment, cancellationToken);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Cannot broadcast comment {CommentId}: it no longer exists")]
    private static partial void LogMissing(ILogger logger, Guid commentId);
}
