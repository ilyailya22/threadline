using Threadline.Comments.Api;
using Threadline.Comments.Api.Extensions;
using Threadline.Comments.Api.Hubs;
using Threadline.Comments.Api.Middleware;
using Threadline.Comments.Application;
using Threadline.Comments.Infrastructure;
using Threadline.Comments.Infrastructure.Messaging;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var environment = builder.Environment;

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "threadline-comments-api"));

// ---------------------------------------------------------------- application and infrastructure
builder.Services
    .AddApplication()
    .AddInfrastructure(configuration)
    .AddInfrastructureInitializer(applyMigrations: true)
    .AddRequestContext()
    .AddSharedDataProtection(configuration)
    .AddAccountAuthentication(configuration)
    .AddAuthorization()
    .AddLoadTestCaptchaBypass(configuration, environment);

// The API hosts only the SignalR fan-out consumers; indexing and image processing belong to the
// worker, which can then be scaled on queue depth independently of the web tier.
//
// Messaging:HostWorkerConsumers=true runs the worker's consumers and the outbox publisher in this
// process as well — the "single container" mode, used by the integration tests (one process
// exercises the whole pipeline) and by a minimal deployment where one process is enough.
var hostWorkerConsumers = configuration.GetValue("Messaging:HostWorkerConsumers", false);

builder.Services.AddMessaging(configuration, bus =>
{
    bus.AddBroadcastConsumers();

    if (hostWorkerConsumers)
    {
        bus.AddBackgroundConsumers();
    }
});

if (hostWorkerConsumers)
{
    builder.Services.AddOutboxPublisher();
}

// ---------------------------------------------------------------- web
builder.Services
    .AddApiControllers()
    .AddExceptionHandler<GlobalExceptionHandler>()
    .AddApiRateLimiting(configuration)
    .AddRealtime(configuration, environment)
    .AddGraphQLApi(environment)
    .AddSpaCors(configuration)
    .AddApiHealthChecks(configuration)
    .AddApiTelemetry(configuration, "threadline-comments-api");

var app = builder.Build();

// Behind Azure Container Apps / nginx the real scheme and client IP arrive in headers. Without
// this the rate limiter partitions every request by the ingress IP and HTTPS redirects loop.
//
// The known-proxy lists have to be emptied, not populated: by default only loopback is trusted, and
// nginx is a different container on an address this app cannot know in advance, so the headers were
// being dropped — the app read every forwarded request as plaintext HTTP from a single client. The
// API has no public ingress, so the only thing that can reach it is the proxy in front of it.
var forwardedHeaders = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2,
};

forwardedHeaders.KnownIPNetworks.Clear();
forwardedHeaders.KnownProxies.Clear();

app.UseForwardedHeaders(forwardedHeaders);

app.UseExceptionHandler();
app.UseSerilogRequestLogging(options => options.GetLevel = (_, elapsedMs, exception) =>
    exception is not null ? LogEventLevel.Error
    : elapsedMs > 1_000 ? LogEventLevel.Warning
    : LogEventLevel.Information);

app.UseSecurityHeaders();
app.UseResponseCompression();
app.UseMiddleware<ClientIdCookieMiddleware>();
app.UseCors(ApiServiceCollectionExtensions.SpaCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseOutputCache();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Threadline Comments API"));
}

app.MapControllers();
app.MapGraphQL();
app.MapHub<CommentsHub>("/hubs/comments");
app.MapHealthEndpoints();

await app.RunAsync();
