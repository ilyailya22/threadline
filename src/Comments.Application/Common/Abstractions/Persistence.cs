using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// Commits the current unit of work. Implemented by the EF Core <c>DbContext</c>, which also turns
/// the domain events collected on tracked entities into outbox rows inside the same transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IUserRepository
{
    /// <summary>Finds an existing identity for the (user name, e-mail) pair the visitor typed.</summary>
    Task<User?> FindAsync(UserName userName, EmailAddress email, CancellationToken cancellationToken = default);

    void Add(User user);
}

public interface ICommentRepository
{
    void Add(Comment comment);

    /// <summary>
    /// Loads the comment being replied to. Only the columns needed to build the child's path are
    /// read, because this sits on the write path of every reply.
    /// </summary>
    Task<Comment?> GetForReplyAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-side access to SQL. Separate from <see cref="ICommentRepository"/> because it returns DTOs
/// with no change tracking, and because it is the <em>fallback</em> path: the top-level table is
/// normally served by Elasticsearch, and this is what answers when the search cluster is down.
/// </summary>
public interface ICommentReadRepository
{
    Task<PagedResult<CommentListItemDto>> GetTopLevelAsync(
        CommentPageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a whole thread in one indexed range scan over <c>(RootId, Path)</c> and assembles the
    /// nested structure in memory — no recursive CTE and no query per level.
    /// </summary>
    Task<CommentThreadDto?> GetThreadAsync(
        Guid rootId,
        int maxDepth,
        int limit,
        string? afterPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommentNodeDto>> GetRepliesAsync(
        Guid parentId,
        CancellationToken cancellationToken = default);

    /// <summary>Batched lookup used by the GraphQL DataLoader to avoid N+1 on nested replies.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CommentNodeDto>>> GetRepliesByParentIdsAsync(
        IReadOnlyList<Guid> parentIds,
        CancellationToken cancellationToken = default);

    Task<CommentNodeDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
