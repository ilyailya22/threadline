using Threadline.Comments.Api.Services;

namespace Threadline.Comments.Api.Middleware;

/// <summary>
/// Issues the first-party client-id cookie.
/// </summary>
/// <remarks>
/// The value is an opaque UUID with no personal data in it, set <c>HttpOnly</c>, <c>SameSite=Lax</c>
/// and <c>Secure</c> outside development. It exists so that "data which helps identify the client"
/// is something better than an IP address that changes with the Wi-Fi — without tracking anyone
/// across sites.
/// </remarks>
public sealed class ClientIdCookieMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Cookies.ContainsKey(HttpClientContext.ClientIdCookie))
        {
            context.Response.Cookies.Append(
                HttpClientContext.ClientIdCookie,
                Guid.CreateVersion7().ToString(),
                new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = !context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
                    Expires = DateTimeOffset.UtcNow.Add(Lifetime),
                    Path = "/",
                });
        }

        await next(context);
    }
}
