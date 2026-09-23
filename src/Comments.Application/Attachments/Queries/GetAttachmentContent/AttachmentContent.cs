namespace Threadline.Comments.Application.Attachments.Queries.GetAttachmentContent;

/// <summary>A stored file, opened for reading.</summary>
/// <param name="Content">The file's bytes. The caller owns the stream and must dispose it.</param>
/// <param name="ContentType">What the bytes are.</param>
/// <param name="DownloadFileName">
/// Set when the file must be downloaded rather than shown in the page — see the handler.
/// </param>
/// <remarks>
/// The bytes behind an attachment URL never change: the file is processed on upload and stored
/// under a path built from its id, so every response may be cached indefinitely.
/// </remarks>
public sealed record AttachmentContent(Stream Content, string ContentType, string? DownloadFileName)
{
    public bool IsDownload => DownloadFileName is not null;
}
