using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Threadline.Comments.Api.GraphQL;
using Threadline.Comments.Api.Middleware;
using Threadline.Comments.Api.Services;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Common;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure;
using Threadline.Comments.Infrastructure.Observability;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;

namespace Threadline.Comments.Api.Extensions;

/// <summary>Service registrations of the web tier, one method per concern.</summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>The CORS policy the SPA is served under.</summary>
    public const string SpaCorsPolicy = "spa";

    /// <summary>Services that expose the current request to the application layer.</summary>
    public static IServiceCollection AddRequestContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IClientContext, HttpClientContext>();
        services.AddSingleton<ICommentNotifier, SignalRCommentNotifier>();

        return services;
    }

    /// <summary>
    /// The CAPTCHA bypass exists so the write load test measures the write path rather than a 400.
    /// It is a decorator over the real service, it needs an explicit secret, and it is not registered
    /// at all in Production — three independent reasons it cannot become a back door.
    /// </summary>
    public static IServiceCollection AddLoadTestCaptchaBypass(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var section = configuration.GetSection(LoadTestOptions.SectionName);
        services.Configure<LoadTestOptions>(section);

        if (!environment.IsProduction() && section.GetValue(nameof(LoadTestOptions.Enabled), false))
        {
            services.Decorate<ICaptchaService, LoadTestCaptchaBypass>();
        }

        return services;
    }

    /// <summary>
    /// How this API writes JSON, wherever it writes it.
    /// </summary>
    /// <remarks>
    /// Enums travel as names ("createdAt", "descending", "Image"), not ordinals: a client should not
    /// have to know that Email happens to be 2, and reordering the enum must not silently change the
    /// meaning of stored URLs. SignalR uses its own serializer options, so it has to be told the
    /// same thing — otherwise the same attachment is <c>"kind": "Image"</c> over REST and
    /// <c>"kind": 1</c> over the hub, and a client that believes the REST shape renders the pushed
    /// one as whatever its fallback branch is.
    /// </remarks>
    private static void ConfigurePayloadJson(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }

    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services
            .AddControllers()
            .AddJsonOptions(options => ConfigurePayloadJson(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options =>
                options.InvalidModelStateResponseFactory = CamelCaseValidationProblem);

        services.AddProblemDetails();
        services.AddOutputCache();
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.AddOpenApi();

        return services;
    }

    /// <summary>
    /// SignalR, with a Redis backplane when Redis is configured. Without the backplane, a client
    /// connected to replica A never sees an event consumed by replica B.
    /// </summary>
    public static IServiceCollection AddRealtime(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var signalR = services
            .AddSignalR(options =>
            {
                options.EnableDetailedErrors = environment.IsDevelopment();
                options.MaximumReceiveMessageSize = 32 * 1024;
            })
            .AddJsonProtocol(options => ConfigurePayloadJson(options.PayloadSerializerOptions));

        if (configuration.GetConnectionString(ConnectionStrings.Redis) is { Length: > 0 } redis)
        {
            signalR.AddStackExchangeRedis(redis, options =>
                options.Configuration.ChannelPrefix = RedisChannel.Literal("threadline-comments"));
        }

        return services;
    }

    public static IServiceCollection AddGraphQLApi(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddGraphQLServer()
            .AddApiTypes()
            .AddDataLoader<RepliesByParentDataLoader>()
            .ModifyRequestOptions(options => options.IncludeExceptionDetails = environment.IsDevelopment())
            .ModifyPagingOptions(options => options.MaxPageSize = 100)
            .AddMaxExecutionDepthRule(12)
            .ModifyCostOptions(options =>
            {
                // A comment tree is recursive, so "replies { replies { … } }" is an unbounded query
                // the schema itself invites. Depth and cost limits turn that from an outage into a 400.
                options.MaxFieldCost = 1_000;
                options.MaxTypeCost = 1_000;
                options.EnforceCostLimits = true;
            });

        return services;
    }

    public static IServiceCollection AddSpaCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(SpaCorsPolicy, policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("X-Captcha-Id", "X-Captcha-Expires-At")

            // Required for the client-id cookie and for SignalR's negotiate request.
            .AllowCredentials()));

        return services;
    }

    /// <summary>
    /// Health checks, split into liveness and readiness.
    /// </summary>
    /// <remarks>
    /// Liveness answers "is this process wedged" and must not consult a database — otherwise a
    /// five-second SQL blip restarts every replica at once and turns a slowdown into an outage.
    /// Readiness answers "should this instance receive traffic" and is where dependency checks belong.
    /// RabbitMQ is covered by the health check MassTransit registers for the bus itself.
    /// </remarks>
    public static IServiceCollection AddApiHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var checks = services.AddHealthChecks();

        checks.AddCheck("self", () => HealthCheckResult.Healthy(), tags: [HealthTags.Live]);

        if (configuration.GetConnectionString(ConnectionStrings.SqlServer) is { Length: > 0 } sql)
        {
            checks.AddSqlServer(sql, name: "sql-server", tags: [HealthTags.Ready]);
        }

        if (configuration.GetConnectionString(ConnectionStrings.Redis) is { Length: > 0 } redis)
        {
            checks.AddRedis(redis, name: "redis", tags: [HealthTags.Ready]);
        }

        if (configuration["Elasticsearch:Url"] is { Length: > 0 } elastic)
        {
            // Not "ready": the list falls back to SQL when search is down, so a degraded search
            // cluster should not take the whole site out of the load balancer.
            checks.AddElasticsearch(elastic, name: "elasticsearch", tags: ["search"]);
        }

        return services;
    }

    /// <summary>The shared telemetry plus request instrumentation and, where configured, Azure Monitor.</summary>
    public static IServiceCollection AddApiTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var telemetry = services.AddCommentsTelemetry(configuration, serviceName)
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation(options =>
            {
                // Health probes fire every few seconds and would otherwise dominate the traces.
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal);
            }))
            .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation());

        if (TelemetryExtensions.UsesAzureMonitor(configuration))
        {
            telemetry.UseAzureMonitor(options =>
                options.ConnectionString = configuration[TelemetryExtensions.AzureMonitorConnectionStringKey]);
        }

        return services;
    }

    /// <summary>
    /// Binding errors ([Required], malformed Guid, unknown enum) are reported by ASP.NET before the
    /// pipeline runs, keyed by C# property name ("UserName"). Everything else — FluentValidation, the
    /// sanitiser — reports camelCase ("userName"), which is what the Angular form controls are called.
    /// One casing everywhere, so every error lands on the right field.
    /// </summary>
    private static BadRequestObjectResult CamelCaseValidationProblem(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        return new BadRequestObjectResult(new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = GlobalExceptionHandler.ValidationTitle,
        });
    }
}
