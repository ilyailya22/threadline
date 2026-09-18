using Threadline.Comments.Application.Common.Abstractions;


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
