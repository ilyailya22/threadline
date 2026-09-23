using System.Security.Claims;
using Threadline.Comments.Application.Common.Abstractions;

namespace Threadline.Comments.Api.Services;

/// <summary>
/// Who the authentication cookie says this request is, or nobody.
/// </summary>
/// <remarks>
/// The id comes from the ticket the server issued and signed, never from the request body — which
/// is the whole point: a caller can put any author id in a form, and none of it reaches the
/// application layer.
/// </remarks>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? Id =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } value
        && Guid.TryParse(value, out var id)
            ? id
            : null;
}
