using Threadline.Comments.Api.Hubs;
using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Threadline.Comments.Api.Services;

/// <inheritdoc cref="ICommentNotifier"/>
public sealed class SignalRCommentNotifier(IHubContext<CommentsHub> hub) : ICommentNotifier
{
    public async Task CommentCreatedAsync(
        CommentNodeDto comment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comment);

        // A top-level comment changes the table everyone is looking at; a reply only concerns the
        // people who have that thread open. Sending each to exactly one group is what keeps the
        // fan-out proportional to interest rather than to the number of connections.
        var group = comment.ParentId is null
            ? CommentsHub.TopLevelGroup
            : CommentsHub.ThreadGroup(comment.RootId);

        await hub.Clients.Group(group)
            .SendAsync(CommentsHub.CommentCreated, comment, cancellationToken);
    }

    public async Task AttachmentReadyAsync(
        Guid commentId,
        AttachmentDto attachment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        await hub.Clients.All.SendAsync(
            CommentsHub.AttachmentReady,
            new { commentId, attachment },
            cancellationToken);
    }
}
