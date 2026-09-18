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

    Task IndexAsync(CommentSearchDocument document, CancellationToken cancellationToken = default);

    Task IndexManyAsync(
        IReadOnlyCollection<CommentSearchDocument> documents,
        CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>The denormalised document stored in Elasticsearch, one per top-level comment.</summary>
public sealed record CommentSearchDocument
{
    public required Guid Id { get; init; }

    public required Guid AuthorId { get; init; }

    public required string UserName { get; init; }

    public required string Email { get; init; }

    public string? HomePage { get; init; }

    public required string TextHtml { get; init; }

    public required string TextPlain { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public int ReplyCount { get; init; }

    public DateTimeOffset? LastReplyAt { get; init; }

    public IReadOnlyList<AttachmentDto> Attachments { get; init; } = [];
}
