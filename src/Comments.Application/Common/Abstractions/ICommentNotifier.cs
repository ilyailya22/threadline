using Threadline.Comments.Application.Comments.Dtos;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>Pushes changes to every browser currently watching (SignalR).</summary>
public interface ICommentNotifier
{
    Task CommentCreatedAsync(CommentNodeDto comment, CancellationToken cancellationToken = default);

    Task AttachmentReadyAsync(
        Guid commentId,
        AttachmentDto attachment,
        CancellationToken cancellationToken = default);
}
