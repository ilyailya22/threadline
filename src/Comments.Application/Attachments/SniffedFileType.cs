using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Attachments;

/// <summary>A file type we are prepared to accept.</summary>
public sealed record SniffedFileType(AttachmentKind Kind, string ContentType, IReadOnlyList<string> Extensions);
