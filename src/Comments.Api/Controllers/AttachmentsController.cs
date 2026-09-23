using Threadline.Comments.Application.Attachments.Queries.GetAttachmentContent;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Threadline.Comments.Api.Controllers;

/// <summary>
/// Serves attachment content.
/// </summary>
/// <remarks>
/// Blobs are private and are streamed through here rather than linked to directly. That buys three
/// things: the storage account is never public, the response headers are ours to set (a stored .txt
/// is sent as a download, never rendered in this origin, which closes the stored-XSS-via-upload
/// path), and the URL is stable and cacheable.
/// </remarks>
[ApiController]
[Route("api/attachments")]
public sealed class AttachmentsController(ISender sender) : ControllerBase
{
    [HttpGet("{id:guid}/content")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<FileStreamResult> GetContent(Guid id, CancellationToken cancellationToken) =>
        StreamAsync(new GetAttachmentContentQuery(id, Thumbnail: false), cancellationToken);

    [HttpGet("{id:guid}/thumbnail")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<FileStreamResult> GetThumbnail(Guid id, CancellationToken cancellationToken) =>
        StreamAsync(new GetAttachmentContentQuery(id, Thumbnail: true), cancellationToken);

    private async Task<FileStreamResult> StreamAsync(GetAttachmentContentQuery query, CancellationToken cancellationToken)
    {
        var file = await sender.Send(query, cancellationToken);

        // The file behind this URL never changes — it is processed on upload and stored under a
        // path built from its id — so browsers and any CDN in front of this can keep it for a year.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        // A download name makes ASP.NET send Content-Disposition: attachment.
        return file.IsDownload
            ? File(file.Content, file.ContentType, file.DownloadFileName)
            : File(file.Content, file.ContentType);
    }
}
