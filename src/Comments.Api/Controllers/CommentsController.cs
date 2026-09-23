using Threadline.Comments.Api.Contracts;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Comments.Queries.GetCommentThread;
using Threadline.Comments.Application.Comments.Queries.GetTopLevelComments;
using Threadline.Comments.Application.Comments.Queries.PreviewComment;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Threadline.Comments.Api.Controllers;

[ApiController]
[Route("api/comments")]
[Produces("application/json")]
public sealed class CommentsController(ISender sender, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// The main page: top-level comments, 25 per page, sortable by user name, e-mail or date in
    /// either direction, newest first by default.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType<PagedResult<CommentListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CommentListItemDto>>> GetTopLevel(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        [FromQuery] CommentSortField sortBy = CommentSortField.CreatedAt,
        [FromQuery] SortDirection direction = SortDirection.Descending,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await sender.Send(
            new GetTopLevelCommentsQuery(
                new CommentPageRequest(page, pageSize, sortBy, direction),
                search),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// One page of a thread, depth-first. Follow <c>nextCursor</c> with <c>after</c> for more.
    /// </summary>
    [HttpGet("{rootId:guid}/thread")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType<CommentThreadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentThreadDto>> GetThread(
        Guid rootId,
        [FromQuery] int maxDepth = CommentPath.MaxDepth,
        [FromQuery] int limit = GetCommentThreadQuery.DefaultLimit,
        [FromQuery] string? after = null,
        CancellationToken cancellationToken = default)
    {
        var thread = await sender.Send(
            new GetCommentThreadQuery(rootId, maxDepth, limit, after),
            cancellationToken);

        return Ok(thread);
    }

    /// <summary>
    /// Renders a message exactly as it will be stored, for the client-side preview.
    /// </summary>
    /// <remarks>
    /// Rate-limited harder than a read: it runs the sanitiser, and it is the one endpoint a client
    /// can call on every keystroke.
    /// </remarks>
    [HttpPost("preview")]
    [EnableRateLimiting(RateLimitPolicies.Preview)]
    [ProducesResponseType<CommentPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CommentPreviewDto>> Preview(
        [FromBody] PreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var preview = await sender.Send(new PreviewCommentQuery(request.Text), cancellationToken);

        return Ok(preview);
    }

    /// <summary>
    /// Posts a comment or a reply, optionally with one image or text file.
    /// </summary>
    /// <remarks>
    /// <c>multipart/form-data</c> so that the file travels with the form in one request. An image is
    /// validated, turned the right way up, downscaled to at most 320×240 and stored before the
    /// comment is — the assignment asks for the picture to be scaled down on upload, and that also
    /// means what is stored is what is served.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(Attachment.MaxImageUploadBytes + (1 * 1024 * 1024))]
    [ProducesResponseType<CreateCommentResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CreateCommentResultDto>> Create(
        [FromForm] CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var file = request.File is { Length: > 0 } posted ? posted : null;

        await using var content = file?.OpenReadStream();

        var upload = file is null || content is null
            ? null
            : new AttachmentUpload(file.FileName, file.ContentType, file.Length, content);

        // The author comes from the cookie, never from the form: a caller may send any name and
        // address it likes, and for a signed-in session none of it is read.
        var result = await sender.Send(request.ToCommand(currentUser.Id, upload), cancellationToken);

        return CreatedAtAction(nameof(GetThread), new { rootId = result.RootId }, result);
    }
}
