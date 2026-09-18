namespace Threadline.Comments.Application.Attachments.Queries.GetAttachmentContent;

/// <summary>A stored file, opened for reading.</summary>
/// <param name="Content">The file's bytes. The caller owns the stream and must dispose it.</param>
/// <param name="ContentType">What the bytes are.</param>
/// <param name="DownloadFileName">
/// Set when the file must be downloaded rather than shown in the page — see the handler.
/// </param>
/// <param name="IsFinal">
/// True once processing is done: the bytes behind this URL will never change again, so they may be
/// cached indefinitely. Before that, the URL still serves the unprocessed upload.
/// </param>
public sealed record AttachmentContent(Stream Content, string ContentType, string? DownloadFileName, bool IsFinal)
{
    public bool IsDownload => DownloadFileName is not null;
}
