using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Attachments;

/// <inheritdoc cref="IAttachmentIntakeService"/>
public sealed class AttachmentIntakeService(
    IFileTypeSniffer sniffer,
    IFileStorage storage,
    IImageProcessor images,
    IDateTimeProvider clock) : IAttachmentIntakeService
{
    private const string FieldName = "file";

    public async Task<Attachment> StageAsync(
        AttachmentUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);

        if (upload.Length <= 0)
        {
            throw new InputValidationException(FieldName, "The uploaded file is empty.");
        }

        if (upload.Length > Attachment.MaxImageUploadBytes)
        {
            throw new InputValidationException(
                FieldName,
                $"The file must not exceed {Attachment.MaxImageUploadBytes / (1024 * 1024)} MB.");
        }

        var header = new byte[FileTypeSniffer.HeaderSize];
        var read = await ReadHeaderAsync(upload.Content, header, cancellationToken);

        var detected = sniffer.Detect(header.AsSpan(0, read))
            ?? throw new InputValidationException(
                FieldName,
                "Only JPG, GIF, PNG images and TXT files are allowed.");

        var extension = Path.GetExtension(upload.FileName);

        if (!detected.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            // The bytes and the name disagree — the hallmark of a disguised upload.
            throw new InputValidationException(
                FieldName,
                $"The file content is {detected.ContentType} but the name ends in '{extension}'.");
        }

        if (detected.Kind == AttachmentKind.TextFile && upload.Length > Attachment.MaxTextFileBytes)
        {
            throw new InputValidationException(
                FieldName,
                $"A text file must not exceed {Attachment.MaxTextFileBytes / 1024} KB.");
        }

        if (upload.Content.CanSeek)
        {
            upload.Content.Position = 0;
        }

        var now = clock.UtcNow;
        var attachmentId = Guid.CreateVersion7(now);

        return detected.Kind == AttachmentKind.Image
            ? await StoreImageAsync(upload, attachmentId, now, cancellationToken)
            : await StoreTextFileAsync(upload, attachmentId, detected, now, cancellationToken);
    }

    /// <summary>
    /// Downscales the image and stores what will be served.
    /// </summary>
    /// <remarks>
    /// This happens in the request, before the comment is saved, because the assignment says an
    /// oversized image is scaled down on upload. Doing it on a worker instead would mean the
    /// original — up to ten megabytes of it — is what the page serves until the worker gets round
    /// to it, and nothing at all if the worker is down. Decoding and resampling a large photograph
    /// costs a few hundred milliseconds of one request; that is the right place to pay it.
    /// </remarks>
    private async Task<Attachment> StoreImageAsync(
        AttachmentUpload upload,
        Guid attachmentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var processed = await images.DownscaleAsync(
            upload.Content,
            Attachment.MaxImageWidth,
            Attachment.MaxImageHeight,
            cancellationToken);

        var storagePath = BuildPath(now, attachmentId, ProcessedImageFormat.DisplayExtension, thumbnail: false);
        var thumbnailPath = BuildPath(now, attachmentId, ProcessedImageFormat.ThumbnailExtension, thumbnail: true);

        await SaveAsync(storagePath, processed.Content, ProcessedImageFormat.DisplayContentType, cancellationToken);
        await SaveAsync(thumbnailPath, processed.Thumbnail, ProcessedImageFormat.ThumbnailContentType, cancellationToken);

        return Attachment.CreateImage(
            ProcessedImageFormat.DisplayContentType,
            upload.FileName,
            processed.Content.LongLength,
            storagePath,
            thumbnailPath,
            processed.Width,
            processed.Height,
            now);
    }

    private async Task<Attachment> StoreTextFileAsync(
        AttachmentUpload upload,
        Guid attachmentId,
        SniffedFileType detected,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var storagePath = BuildPath(now, attachmentId, detected.Extensions[0], thumbnail: false);

        await storage.SaveAsync(storagePath, upload.Content, detected.ContentType, cancellationToken);

        return Attachment.CreateTextFile(upload.FileName, upload.Length, storagePath, now);
    }

    /// <summary>
    /// Blob layout: <c>files|thumbnails/yyyy/MM/dd/{id}{ext}</c>. Date-partitioned so that listing,
    /// lifecycle rules and cold-tier archiving stay cheap once the container holds millions of blobs.
    /// The path is built entirely from server-side values; the user's file name is kept on the
    /// entity for display and never participates in a path.
    /// </summary>
    public static string BuildPath(
        DateTimeOffset now,
        Guid attachmentId,
        string extension,
        bool thumbnail) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{(thumbnail ? "thumbnails" : "files")}/{now:yyyy/MM/dd}/{attachmentId:N}{extension}");

    private async Task SaveAsync(
        string path,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);

        await storage.SaveAsync(path, stream, contentType, cancellationToken);
    }

    private static async Task<int> ReadHeaderAsync(
        Stream content,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await content.ReadAsync(buffer.AsMemory(total), cancellationToken);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
