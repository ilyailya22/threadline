using Threadline.Comments.Application.Captcha;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Threadline.Comments.Api.Controllers;

[ApiController]
[Route("api/captcha")]
public sealed class CaptchaController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Issues a challenge: an opaque id in a header and the image in the body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returning the PNG directly rather than base64 inside JSON keeps the payload a third smaller
    /// and lets the browser treat it as an ordinary image — the form points an <c>&lt;img&gt;</c> at
    /// this URL and the id comes back in the <c>X-Captcha-Id</c> header.
    /// </para>
    /// <para>
    /// Caching is disabled explicitly. A cached CAPTCHA is not a CAPTCHA.
    /// </para>
    /// </remarks>
    [HttpGet]
    [EnableRateLimiting(RateLimitPolicies.Captcha)]
    [Produces("image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Issue(CancellationToken cancellationToken)
    {
        var challenge = await sender.Send(new IssueCaptchaQuery(), cancellationToken);

        Response.Headers["X-Captcha-Id"] = challenge.Id.ToString();
        Response.Headers["X-Captcha-Expires-At"] = challenge.ExpiresAt.ToString("O");
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";

        return File(challenge.ImagePng, "image/png");
    }
}
