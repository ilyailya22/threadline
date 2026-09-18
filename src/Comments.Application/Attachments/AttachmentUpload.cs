namespace Threadline.Comments.Application.Attachments;

/// <summary>A file as it arrives from the browser, before anything has been decided about it.</summary>
public sealed record AttachmentUpload(
    string FileName,
    string? DeclaredContentType,
    long Length,
    Stream Content);
