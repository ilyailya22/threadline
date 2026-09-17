using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>Injectable clock — the reason every test in this solution is deterministic.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Request-scoped facts about the caller, supplied by the API layer. Keeping this behind an
/// interface is what lets the application layer record who posted a comment without referencing
/// <c>HttpContext</c>.
/// </summary>
public interface IClientContext
{
    /// <summary>Hex HMAC-SHA256 of the remote IP — see <see cref="ClientFingerprint"/>.</summary>
    string IpHash { get; }

    string? UserAgent { get; }

    Guid? ClientId { get; }
}

/// <summary>Object storage for attachments — Azure Blob Storage in production, Azurite locally.</summary>
public interface IFileStorage
{
    Task SaveAsync(
        string path,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(string path, CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Public URL for a stored file. In Azure this is a short-lived read-only SAS URL, so blobs are
    /// never world-readable and a leaked link expires on its own.
    /// </summary>
    string GetUrl(string path);
}

/// <summary>Turns stored attachments into the shape the client renders.</summary>
public interface IAttachmentUrlBuilder
{
    AttachmentDto ToDto(Attachment attachment);
}

/// <summary>Pushes a freshly accepted comment to every browser watching the thread (SignalR).</summary>
public interface ICommentNotifier
{
    Task CommentCreatedAsync(CommentNodeDto comment, CancellationToken cancellationToken = default);

    Task AttachmentReadyAsync(
        Guid commentId,
        AttachmentDto attachment,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Caches pages of the top-level comment list.
/// </summary>
/// <remarks>
/// The interface is get-or-create rather than get/set on purpose. Under the traffic this system is
/// sized for, a plain get/set has a cache-stampede failure mode: the moment a hot key expires, every
/// concurrent request misses at once and they all run the same expensive query. Handing the factory
/// to the cache lets the implementation collapse those into one.
/// </remarks>
public interface ICommentCache
{
    Task<PagedCacheEntry> GetOrCreateTopLevelAsync(
        string key,
        Func<CancellationToken, ValueTask<PagedCacheEntry>> factory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every cached list page. Called on each new top-level comment: with LIFO ordering the
    /// first page changes on every insert anyway, so per-key invalidation would buy nothing and a
    /// tag-based flush keeps the cache honest.
    /// </summary>
    Task InvalidateTopLevelAsync(CancellationToken cancellationToken = default);
}

/// <summary>Cached page payload. A record so the cache can serialise it without reflection surprises.</summary>
public sealed record PagedCacheEntry(
    IReadOnlyList<CommentListItemDto> Items,
    int Page,
    int PageSize,
    long TotalCount);
