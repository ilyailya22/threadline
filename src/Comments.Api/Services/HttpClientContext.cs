using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Common;

namespace Threadline.Comments.Api.Services;

/// <summary>
/// Supplies the application layer with the request-scoped facts it needs to identify a client,
/// without letting it see <c>HttpContext</c>.
/// </summary>
public sealed class HttpClientContext : IClientContext
{
    /// <summary>Name of the first-party cookie that gives a browser a stable, opaque identity.</summary>
    public const string ClientIdCookie = "cid";

    public HttpClientContext(IHttpContextAccessor accessor, IIpAddressHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(hasher);

        var context = accessor.HttpContext;

        IpHash = hasher.Hash(context?.Connection.RemoteIpAddress?.ToString());

        UserAgent = context?.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent
            ? agent
            : null;

        ClientId = context?.Request.Cookies.TryGetValue(ClientIdCookie, out var raw) == true
            && Guid.TryParse(raw, out var parsed)
                ? parsed
                : null;
    }

    public string IpHash { get; }

    public string? UserAgent { get; }

    public Guid? ClientId { get; }
}

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
