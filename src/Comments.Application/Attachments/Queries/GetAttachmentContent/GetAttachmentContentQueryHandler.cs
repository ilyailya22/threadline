using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Comments;
using MediatR;

namespace Threadline.Comments.Application.Attachments.Queries.GetAttachmentContent;

public sealed class GetAttachmentContentQueryHandler(IAttachmentRepository attachments, IFileStorage storage)
    : IRequestHandler<GetAttachmentContentQuery, AttachmentContent>
{
    public async Task<AttachmentContent> Handle(
        GetAttachmentContentQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attachment = await attachments.FindAsync(request.AttachmentId, cancellationToken)
            ?? throw NotFound(request);

        // Text files have no thumbnail.
        var path = request.Thumbnail ? attachment.ThumbnailPath : attachment.StoragePath;

        var content = (path is null ? null : await storage.OpenReadAsync(path, cancellationToken))
            ?? throw NotFound(request);

        if (request.Thumbnail)
        {
            return new AttachmentContent(content, ProcessedImageFormat.ThumbnailContentType, DownloadFileName: null);
        }

        // Text files are always downloaded, never rendered. An HTML payload saved as .txt would
        // otherwise execute in this origin — the whole stored-XSS-by-upload attack.
        var downloadName = attachment.Kind == AttachmentKind.TextFile ? attachment.OriginalFileName : null;

        return new AttachmentContent(content, attachment.ContentType, downloadName);
    }

    private static NotFoundException NotFound(GetAttachmentContentQuery request) =>
        new(request.Thumbnail ? "Thumbnail" : nameof(Attachment), request.AttachmentId);
}
