using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

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
public sealed class AttachmentsController(AppDbContext context, IFileStorage storage) : ControllerBase
{
    [HttpGet("{id:guid}/content")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken) =>
        StreamAsync(id, thumbnail: false, cancellationToken);

    [HttpGet("{id:guid}/thumbnail")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetThumbnail(Guid id, CancellationToken cancellationToken) =>
        StreamAsync(id, thumbnail: true, cancellationToken);

    private async Task<IActionResult> StreamAsync(Guid id, bool thumbnail, CancellationToken cancellationToken)
    {
        var attachment = await context.Attachments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (attachment is null)
        {
            return NotFound();
        }

        var path = thumbnail ? attachment.ThumbnailPath : attachment.StoragePath;

        if (path is null)
        {
            return NotFound();
        }

        var stream = await storage.OpenReadAsync(path, cancellationToken);

        if (stream is null)
        {
            return NotFound();
        }

        var contentType = thumbnail
            ? "image/webp"
            : attachment.ContentType;

        // Immutable: the blob for a given attachment id never changes once processed, so browsers
        // and any CDN in front of this can keep it for a year.
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";

        // Text files are always downloaded, never rendered. An HTML payload with a .txt extension
        // would otherwise execute in this origin, which is the whole stored-XSS-by-upload attack.
        return attachment.Kind == AttachmentKind.TextFile && !thumbnail
            ? File(stream, contentType, attachment.OriginalFileName)
            : File(stream, contentType);
    }
}
