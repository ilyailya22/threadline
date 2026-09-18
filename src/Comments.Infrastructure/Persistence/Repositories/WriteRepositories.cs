using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> FindAsync(
        UserName userName,
        EmailAddress email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(email);

        // Hits UX_Users_UserName_Email. The comparison is case-insensitive because the column
        // collation is, which matches how UserName and EmailAddress define their own equality.
        // Tracked: the user is modified (last activity) and becomes the Author of a new comment. An
        // untracked instance would be seen as a new entity and inserted a second time.
        return context.Users.AsTracking().FirstOrDefaultAsync(
            u => u.UserName == userName && u.Email == email,
            cancellationToken);
    }

    public void Add(User user) => context.Users.Add(user);
}

public sealed class CommentRepository(AppDbContext context) : ICommentRepository
{
    public void Add(Comment comment) => context.Comments.Add(comment);

    /// <summary>
    /// Loads the parent of a reply. Tracked, because the new comment's path is derived from it, but
    /// nothing else about the parent is needed — no attachments, no author, no subtree.
    /// </summary>
    public Task<Comment?> GetForReplyAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Comments.AsTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Comments.AnyAsync(c => c.Id == id, cancellationToken);
}
