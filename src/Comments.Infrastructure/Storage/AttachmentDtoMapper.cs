using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Infrastructure.Storage;

/// <summary>
/// Maps a stored attachment to what the client needs to render it.
/// </summary>
/// <remarks>
/// URLs are built from the attachment id only. The user's original file name is carried in the DTO
/// for display, but it never appears in a path — which is what makes a file called
/// <c>../../index.html</c> a display curiosity rather than a traversal bug.
/// </remarks>
public sealed class AttachmentDtoMapper : IAttachmentDtoMapper
{
    public const string BasePath = "/api/attachments";

    public AttachmentDto ToDto(Attachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return new AttachmentDto(
            attachment.Id,
            attachment.Kind,
            attachment.Status,
            attachment.ContentType,
            attachment.OriginalFileName,
            attachment.SizeBytes,
            $"{BasePath}/{attachment.Id}/content",
            attachment.ThumbnailPath is null ? null : $"{BasePath}/{attachment.Id}/thumbnail",
            attachment.Width,
            attachment.Height);
    }
}
