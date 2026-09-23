using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Models;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// The Elasticsearch read model for top-level comments.
/// </summary>
/// <remarks>
/// <para>
/// This is the answer to "what serves the list at a million comments and a hundred thousand daily
/// visitors". Every page of the table — any of the three sort fields, either direction — is one
/// Elasticsearch query against a denormalised document that already carries the author's name and
/// e-mail and the reply counter. SQL never participates in a list request.
/// </para>
/// <para>
/// The index is populated asynchronously from the outbox, so it is eventually consistent. The one
/// place where that is visible to a user — their own comment not being in the list immediately
/// after posting — is handled in the UI by inserting the comment optimistically from the SignalR
/// echo, not by making the write path wait for the index.
/// </para>
/// </remarks>
public interface ICommentSearchIndex
{
    /// <summary>Creates the index and its alias if they do not exist. Idempotent; runs at startup.</summary>
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<CommentListItemDto>> QueryTopLevelAsync(
        CommentPageRequest request,
        string? searchText = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// When the newest indexed top-level comment was created, or <see langword="null"/> for an
    /// empty index. Compared against SQL to tell a lagging projection from a healthy one.
    /// </summary>
    Task<DateTimeOffset?> GetNewestCreatedAtAsync(CancellationToken cancellationToken = default);

    Task IndexManyAsync(
        IReadOnlyCollection<CommentSearchDocument> documents,
        CancellationToken cancellationToken = default);
}
