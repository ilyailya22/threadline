using Azure.Monitor.OpenTelemetry.AspNetCore;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Threadline.Comments.Api.Extensions;

public static class StartupExtensions
{
    /// <summary>
    /// Health checks, split into liveness and readiness.
    /// </summary>
    /// <remarks>
    /// The distinction matters in Container Apps and Kubernetes. Liveness answers "is this process
    /// wedged" and must not consult a database — otherwise a five-second SQL blip restarts every
    /// replica at once and turns a slowdown into an outage. Readiness answers "should this instance
    /// receive traffic" and is exactly where dependency checks belong.
    /// </remarks>
    public static IServiceCollection AddApiHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var checks = services.AddHealthChecks();

        checks.AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
            tags: ["live"]);

        if (configuration.GetConnectionString("SqlServer") is { Length: > 0 } sql)
        {
            checks.AddSqlServer(sql, name: "sql-server", tags: ["ready"]);
        }

        if (configuration.GetConnectionString("Redis") is { Length: > 0 } redis)
        {
            checks.AddRedis(redis, name: "redis", tags: ["ready"]);
        }

        // RabbitMQ is not registered here: MassTransit already publishes a "masstransit-bus" health
        // check that reports the state of the bus and its endpoints, which is strictly more useful
        // than "can I open a TCP connection to the broker".

        if (configuration["Elasticsearch:Url"] is { Length: > 0 } elastic)
        {
            // Not tagged "ready": the top-level list falls back to SQL when search is down, so a
            // degraded search cluster should not take the whole site out of the load balancer.
            checks.AddElasticsearch(elastic, name: "elasticsearch", tags: ["search"]);
        }

        return services;
    }

    /// <summary>
    /// OpenTelemetry traces and metrics, exported to Azure Monitor when configured and to an OTLP
    /// endpoint otherwise (Aspire dashboard, Jaeger, Grafana — whatever is running locally).
    /// </summary>
    public static IServiceCollection AddApiTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Health probes fire every few seconds and would otherwise dominate the traces.
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal);
                })
                .AddHttpClientInstrumentation()
                .AddSource("MassTransit"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (configuration["ApplicationInsights:ConnectionString"] is { Length: > 0 })
        {
            telemetry.UseAzureMonitor(options =>
                options.ConnectionString = configuration["ApplicationInsights:ConnectionString"]);
        }
        else if (configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 })
        {
            telemetry.UseOtlpExporter();
        }

        return services;
    }

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
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-site";

            headers["Content-Security-Policy"] =
                "default-src 'none'; "
                + "img-src 'self' data:; "
                + "style-src 'self'; "
                + "script-src 'self'; "
                + "connect-src 'self'; "
                + "font-src 'self'; "
                + "frame-ancestors 'none'; "
                + "base-uri 'none'; "
                + "form-action 'self'";

            await next();
        });
    }

    /// <summary>
    /// One-time startup work: apply migrations, create the search index, create the blob container.
    /// </summary>
    /// <remarks>
    /// Running migrations from the application is a deliberate choice for a system deployed as a
    /// single unit: <c>docker compose up</c> and a container-apps revision both come up working with
    /// no manual step, which is exactly what the assignment's "start it from your README" check
    /// exercises. In a multi-team production system this belongs in a release pipeline step
    /// instead — noted in docs/IMPROVEMENTS.md rather than pretended away.
    /// </remarks>
    public static async Task InitializeInfrastructureAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        using var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILogger<WebApplication>>();

        var context = provider.GetRequiredService<AppDbContext>();
        await context.Database.MigrateAsync();
        logger.LogInformation("Database schema is up to date");

        try
        {
            await provider.GetRequiredService<ICommentSearchIndex>().EnsureCreatedAsync();
        }
        catch (Exception exception)
        {
            // Search being unavailable at startup is survivable — the list falls back to SQL — so
            // this must not stop the API from serving.
            logger.LogWarning(exception, "Could not initialise the Elasticsearch index at startup");
        }

        try
        {
            await provider.GetRequiredService<BlobFileStorage>().EnsureCreatedAsync();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not initialise the blob container at startup");
        }
    }
}
