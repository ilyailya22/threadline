using System.Text.Json.Serialization;
using Threadline.Comments.Api;
using Threadline.Comments.Api.Consumers;
using Threadline.Comments.Api.Extensions;
using Threadline.Comments.Api.GraphQL;
using Threadline.Comments.Api.Hubs;
using Threadline.Comments.Api.Middleware;
using Threadline.Comments.Api.Services;
using Threadline.Comments.Application;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Common;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure;
using Threadline.Comments.Infrastructure.Messaging.Consumers;
using Threadline.Comments.Infrastructure.Search;
using Threadline.Comments.Infrastructure.Storage;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------- logging
//
// Serilog is configured from appsettings so the log level can be changed per environment without a
// rebuild; the bootstrap logger means startup failures are still logged properly.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "threadline-comments-api"));

// ---------------------------------------------------------------- application
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// The API hosts only the SignalR fan-out consumer. Indexing and image processing belong to the
// worker, which can then be scaled for throughput independently of the web tier.
builder.Services.AddMessaging(
    builder.Configuration,
    bus =>
    {
        bus.AddConsumer<CommentBroadcastConsumer>();
        bus.AddConsumer<AttachmentReadyBroadcastConsumer>();
    });

// The CAPTCHA bypass exists so the write load test measures the write path rather than a 400.
// It is a decorator over the real service, it needs an explicit secret, and it is not registered at
// all outside development — three independent reasons it cannot become a production back door.
builder.Services.Configure<LoadTestOptions>(
    builder.Configuration.GetSection(LoadTestOptions.SectionName));

if (!builder.Environment.IsProduction()
    && builder.Configuration.GetValue($"{LoadTestOptions.SectionName}:Enabled", false))
{
    builder.Services.Decorate<ICaptchaService, LoadTestCaptchaBypass>();
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IClientContext, HttpClientContext>();
builder.Services.AddSingleton<ICommentNotifier, SignalRCommentNotifier>();

// ---------------------------------------------------------------- web
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums travel as names ("createdAt", "descending"), not ordinals: a client reading the API
        // should not have to know that Email happens to be 2, and reordering the enum must not
        // silently change the meaning of stored URLs.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiRateLimiting();
builder.Services.AddOutputCache();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.AddOpenApi();

// ---------------------------------------------------------------- SignalR
var redisConnection = builder.Configuration.GetConnectionString("Redis");

var signalR = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 32 * 1024;
});

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    // Without the backplane, a client connected to replica A never sees an event consumed by
    // replica B. With it, the number of API replicas stops being something the UI can notice.
    signalR.AddStackExchangeRedis(redisConnection, options => options.Configuration.ChannelPrefix =
        StackExchange.Redis.RedisChannel.Literal("threadline-comments"));
}

// ---------------------------------------------------------------- GraphQL
builder.Services
    .AddGraphQLServer()
    .AddApiTypes()
    .AddDataLoader<RepliesByParentDataLoader>()
    .ModifyRequestOptions(options =>
    {
        options.IncludeExceptionDetails = builder.Environment.IsDevelopment();
    })
    .ModifyPagingOptions(options => options.MaxPageSize = 100)
    .AddMaxExecutionDepthRule(12)
    .ModifyCostOptions(options =>
    {
        // A comment tree is recursive, so "replies { replies { … } }" is an unbounded query the
        // schema itself invites. Depth and cost limits turn that from an outage into a 400.
        options.MaxFieldCost = 1_000;
        options.MaxTypeCost = 1_000;
        options.EnforceCostLimits = true;
    });

// ---------------------------------------------------------------- CORS
const string CorsPolicy = "spa";

builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    policy
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("X-Captcha-Id", "X-Captcha-Expires-At")

        // Required for the client-id cookie and for SignalR's negotiate request.
        .AllowCredentials();
}));

// ---------------------------------------------------------------- health
builder.Services.AddApiHealthChecks(builder.Configuration);

// ---------------------------------------------------------------- observability
builder.Services.AddApiTelemetry(builder.Configuration, "threadline-comments-api");

var app = builder.Build();

// Behind Azure Container Apps / nginx the real scheme and client IP arrive in headers. Without
// this the rate limiter partitions every request by the ingress IP and HTTPS redirects loop.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2,
});

app.UseExceptionHandler();
app.UseSerilogRequestLogging(options => options.GetLevel = (_, elapsed, exception) =>
    exception is not null ? Serilog.Events.LogEventLevel.Error
    : elapsed > 1_000 ? Serilog.Events.LogEventLevel.Warning
    : Serilog.Events.LogEventLevel.Information);

app.UseSecurityHeaders();
app.UseResponseCompression();
app.UseMiddleware<ClientIdCookieMiddleware>();
app.UseCors(CorsPolicy);
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

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    // Liveness must not depend on anything external: a failing dependency should take traffic away
    // from the instance (readiness), not make the orchestrator kill and restart it in a loop.
    Predicate = registration => registration.Tags.Contains("live"),
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
});

await app.InitializeInfrastructureAsync();

await app.RunAsync();

/// <summary>Exposed so the integration tests can spin the real pipeline up with WebApplicationFactory.</summary>
public partial class Program;
