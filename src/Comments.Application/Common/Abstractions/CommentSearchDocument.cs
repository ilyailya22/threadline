using Threadline.Comments.Application.Comments.Dtos;

namespace Threadline.Comments.Application.Common.Abstractions;

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
