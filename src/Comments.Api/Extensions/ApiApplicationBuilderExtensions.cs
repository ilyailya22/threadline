using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Threadline.Comments.Api.Extensions;

/// <summary>Middleware and endpoint mapping of the web tier.</summary>
public static class ApiApplicationBuilderExtensions
{
    private const string ContentSecurityPolicy =
        "default-src 'none'; "
        + "img-src 'self' data:; "
        + "style-src 'self'; "
        + "script-src 'self'; "
        + "connect-src 'self'; "
        + "font-src 'self'; "
        + "frame-ancestors 'none'; "
        + "base-uri 'none'; "
        + "form-action 'self'";

    /// <summary>
    /// Security headers applied to every response.
    /// </summary>
    /// <remarks>
    /// The assignment calls out XSS explicitly. The sanitiser is the primary defence; this is the
    /// second line, so that a bug in the first one is contained rather than exploitable. The CSP has
    /// no <c>unsafe-inline</c> for scripts, which is what makes an injected <c>&lt;script&gt;</c>
    /// inert even if one ever reached the page.
    /// </remarks>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            await next();
        });
    }

    /// <summary>
    /// Liveness must not depend on anything external: a failing dependency should take traffic away
    /// from the instance (readiness), not make the orchestrator restart it in a loop.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Live),
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthTags.Ready),
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        });

        return endpoints;
    }
}
