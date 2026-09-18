using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Comments.Dtos;

/// <summary>A file attached to a comment, as the client needs it for the lightbox.</summary>
public sealed record AttachmentDto(
    Guid Id,
    AttachmentKind Kind,
    AttachmentStatus Status,
    string ContentType,
    string OriginalFileName,
    long SizeBytes,
    string Url,
    string? ThumbnailUrl,
    int? Width,
    int? Height);
