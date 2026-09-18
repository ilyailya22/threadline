using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Common.Abstractions;

public interface ICommentRepository
{
    void Add(Comment comment);

    /// <summary>
    /// Loads the comment being replied to. Only the comment itself is read, because this sits on the
    /// write path of every reply and the child needs nothing but the parent's path.
    /// </summary>
    Task<Comment?> GetForReplyAsync(Guid id, CancellationToken cancellationToken = default);
}
