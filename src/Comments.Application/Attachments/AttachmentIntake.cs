using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Attachments;

/// <summary>A file as it arrives from the browser, before anything has been decided about it.</summary>
public sealed record AttachmentUpload(
    string FileName,
    string? DeclaredContentType,
    long Length,
    Stream Content);

/// <summary>
/// Validates an uploaded file and puts it in object storage, returning the domain entity.
/// </summary>
/// <remarks>
/// Deliberately splits "accept" from "process". Acceptance is cheap and synchronous — sniff the
/// bytes, check the limits, stream to Blob Storage — so the user gets an answer immediately.
/// Downscaling the image to 320×240 and making a thumbnail is expensive and happens on a worker,
/// triggered by the comment's outbox event. That is what keeps a burst of uploads from consuming
/// the API's threads.
/// </remarks>
public interface IAttachmentIntakeService
{
    Task<Attachment> StageAsync(AttachmentUpload upload, CancellationToken cancellationToken = default);
}

/// <summary>Detects the real type of an uploaded file from its content, not its name.</summary>
public interface IFileTypeSniffer
{
    /// <summary>
    /// Returns the type the bytes actually are, or <see langword="null"/> when they are none of the
    /// allowed types.
    /// </summary>
    SniffedFileType? Detect(ReadOnlySpan<byte> header);
}

/// <summary>A file type we are prepared to accept.</summary>
public sealed record SniffedFileType(AttachmentKind Kind, string ContentType, string[] Extensions);
