using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Threadline.Comments.Api.Contracts;
using Threadline.Comments.Application.Accounts.Commands.RemoveAvatar;
using Threadline.Comments.Application.Accounts.Commands.SetAvatar;
using Threadline.Comments.Application.Accounts.Commands.UpdateProfile;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Accounts.Queries.GetAvatar;
using Threadline.Comments.Application.Attachments;

namespace Threadline.Comments.Api.Controllers;

/// <summary>Account settings, and the avatars everyone reads.</summary>
[ApiController]
[Route("api/accounts")]
public sealed class AccountsController(ISender sender) : ControllerBase
{
    /// <summary>Changes the nickname and the home page.</summary>
    [HttpPut("me")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<AccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountDto>> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await sender.Send(
            new UpdateProfileCommand(request.UserName, request.HomePage),
            cancellationToken);
    }

    /// <summary>Uploads a new avatar.</summary>
    [HttpPost("me/avatar")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [RequestSizeLimit(Domain.Comments.Attachment.MaxImageUploadBytes + (1 * 1024 * 1024))]
    [ProducesResponseType<AccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountDto>> SetAvatar(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        await using var content = file.OpenReadStream();

        return await sender.Send(
            new SetAvatarCommand(new AttachmentUpload(file.FileName, file.ContentType, file.Length, content)),
            cancellationToken);
    }

    /// <summary>Drops the uploaded avatar, falling back to Google's picture or to initials.</summary>
    [HttpDelete("me/avatar")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Write)]
    [ProducesResponseType<AccountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDto>> RemoveAvatar(CancellationToken cancellationToken) =>
        await sender.Send(new RemoveAvatarCommand(), cancellationToken);

    /// <summary>
    /// Serves an account's avatar. Public, because it is drawn next to every comment they wrote.
    /// </summary>
    [HttpGet("{id:guid}/avatar")]
    [EnableRateLimiting(RateLimitPolicies.Read)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Avatar(Guid id, CancellationToken cancellationToken)
    {
        var avatar = await sender.Send(new GetAvatarQuery(id), cancellationToken);

        // The path carries the moment it was uploaded, so this URL changes whenever the picture
        // does and a long cache can never show yesterday's face.
        Response.Headers.CacheControl = "public, max-age=604800";

        return File(avatar.Content, avatar.ContentType);
    }
}
