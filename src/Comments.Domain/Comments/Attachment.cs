using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Comments;

/// <summary>
/// A file attached to a comment: an image or a plain-text file.
/// </summary>
public sealed class Attachment : Entity
{
    /// <summary>Maximum displayed image width required by the assignment.</summary>
    public const int MaxImageWidth = 320;

    /// <summary>Maximum displayed image height required by the assignment.</summary>
    public const int MaxImageHeight = 240;

    /// <summary>Maximum size of a .txt attachment required by the assignment: 100 KB.</summary>
    public const int MaxTextFileBytes = 100 * 1024;

    /// <summary>
    /// Maximum size accepted for an <em>uploaded</em> image before downscaling. Large images are
    /// allowed in — the assignment says they must be scaled down, not rejected — but not unbounded.
    /// </summary>
    public const int MaxImageUploadBytes = 10 * 1024 * 1024;

    public const int MaxFileNameLength = 260;

    private Attachment()
    {
        // EF Core
    }

    private Attachment(
        Guid id,
        AttachmentKind kind,
        string contentType,
        string originalFileName,
        long sizeBytes,
        string storagePath,
        DateTimeOffset createdAt)
        : base(id)
    {
        Kind = kind;
        ContentType = contentType;
        OriginalFileName = originalFileName;
        SizeBytes = sizeBytes;
        StoragePath = storagePath;
        Status = AttachmentStatus.Pending;
        CreatedAt = createdAt;
    }

    public Guid CommentId { get; private set; }

    public AttachmentKind Kind { get; private set; }

    public AttachmentStatus Status { get; private set; }

    public string ContentType { get; private set; } = null!;

    /// <summary>The name the user's file had, kept for display and download only — never used to build a path.</summary>
    public string OriginalFileName { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    /// <summary>Blob path of the servable file (downscaled, for images).</summary>
    public string StoragePath { get; private set; } = null!;

    /// <summary>Blob path of the lightbox thumbnail; <see langword="null"/> for text files.</summary>
    public string? ThumbnailPath { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? FailureReason { get; private set; }

    public static Attachment CreateImage(
        string contentType,
        string originalFileName,
        long sizeBytes,
        string storagePath,
        DateTimeOffset now)
    {
        if (sizeBytes is <= 0 or > MaxImageUploadBytes)
        {
            throw new DomainException(
                $"An image must be between 1 byte and {MaxImageUploadBytes / (1024 * 1024)} MB.");
        }

        return new Attachment(
            Guid.CreateVersion7(now),
            AttachmentKind.Image,
            contentType,
            Sanitize(originalFileName),
            sizeBytes,
            storagePath,
            now);
    }

    public static Attachment CreateTextFile(
        string originalFileName,
        long sizeBytes,
        string storagePath,
        DateTimeOffset now)
    {
        if (sizeBytes is <= 0 or > MaxTextFileBytes)
        {
            throw new DomainException(
                $"A text file must be between 1 byte and {MaxTextFileBytes / 1024} KB.");
        }

        return new Attachment(
            Guid.CreateVersion7(now),
            AttachmentKind.TextFile,
            "text/plain",
            Sanitize(originalFileName),
            sizeBytes,
            storagePath,
            now);
    }

    /// <summary>Called by the worker once the image has been downscaled and a thumbnail produced.</summary>
    public void MarkImageProcessed(
        string storagePath,
        string contentType,
        string thumbnailPath,
        int width,
        int height,
        long sizeBytes,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (Kind != AttachmentKind.Image)
        {
            throw new DomainException("Only image attachments can be marked as image-processed.");
        }

        if (width is <= 0 or > MaxImageWidth || height is <= 0 or > MaxImageHeight)
        {
            throw new DomainException(
                $"A processed image must fit into {MaxImageWidth}×{MaxImageHeight} pixels, got {width}×{height}.");
        }

        // The stored file is re-encoded, so its type is the processor's output format, not what was
        // uploaded. Keeping the upload's type would serve a PNG labelled image/jpeg.
        StoragePath = storagePath;
        ContentType = contentType;
        ThumbnailPath = thumbnailPath;
        Width = width;
        Height = height;
        SizeBytes = sizeBytes;
        Status = AttachmentStatus.Ready;
        ProcessedAt = now;
        FailureReason = null;
    }

    public void MarkTextProcessed(DateTimeOffset now)
    {
        if (Kind != AttachmentKind.TextFile)
        {
            throw new DomainException("Only text attachments can be marked as text-processed.");
        }

        Status = AttachmentStatus.Ready;
        ProcessedAt = now;
        FailureReason = null;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Status = AttachmentStatus.Failed;
        FailureReason = reason[..Math.Min(reason.Length, 1024)];
        ProcessedAt = now;
    }

    internal void AttachTo(Guid commentId) => CommentId = commentId;

    /// <summary>
    /// Strips any directory component and control characters. The stored name is display-only, but
    /// a value like <c>../../web.config</c> must never reach a path API or a Content-Disposition
    /// header, so it is neutralised at the boundary of the domain rather than at every use site.
    /// </summary>
    private static string Sanitize(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new DomainException("File name is required.");
        }

        var name = fileName.Replace('\\', '/');
        var lastSlash = name.LastIndexOf('/');
        if (lastSlash >= 0)
        {
            name = name[(lastSlash + 1)..];
        }

        name = new string([.. name.Where(c => !char.IsControl(c) && c != '"')]).Trim();

        if (name.Length == 0)
        {
            throw new DomainException("File name is required.");
        }

        return name[..Math.Min(name.Length, MaxFileNameLength)];
    }
}
