using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Attachments;

/// <inheritdoc cref="IAttachmentIntakeService"/>
public sealed class AttachmentIntakeService(
    IFileTypeSniffer sniffer,
    IFileStorage storage,
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

        var now = clock.UtcNow;

        // The stored path is built entirely from server-side values. The user's file name is kept
        // on the entity for display, but never participates in a path.
        var attachmentId = Guid.CreateVersion7(now);
        var storagePath = BuildPath(now, attachmentId, detected, original: true);

        if (upload.Content.CanSeek)
        {
            upload.Content.Position = 0;
        }

        await storage.SaveAsync(storagePath, upload.Content, detected.ContentType, cancellationToken);

        return detected.Kind == AttachmentKind.Image
            ? Attachment.CreateImage(
                detected.ContentType,
                upload.FileName,
                upload.Length,
                storagePath,
                now)
            : Attachment.CreateTextFile(
                upload.FileName,
                upload.Length,
                storagePath,
                now);
    }

    /// <summary>
    /// Blob layout: <c>originals|files/yyyy/MM/dd/{id}{ext}</c>. Date-partitioned so that listing,
    /// lifecycle rules and cold-tier archiving stay cheap once the container holds millions of blobs.
    /// </summary>
    public static string BuildPath(
        DateTimeOffset now,
        Guid attachmentId,
        SniffedFileType type,
        bool original) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{(original ? "originals" : "files")}/{now:yyyy/MM/dd}/{attachmentId:N}{type.Extensions[0]}");

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
