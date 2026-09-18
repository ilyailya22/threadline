using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Comments;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Persistence.Repositories;

public sealed class CommentRepository(AppDbContext context) : ICommentRepository
{
    public void Add(Comment comment) => context.Comments.Add(comment);

    /// <summary>
    /// Loads the parent of a reply. Tracked, because the new comment's path is derived from it, but
    /// nothing else about the parent is needed — no attachments, no author, no subtree.
    /// </summary>
    public Task<Comment?> GetForReplyAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Comments.AsTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
}
