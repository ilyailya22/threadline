using Threadline.Comments.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Threadline.Comments.Worker;

/// <summary>
/// Makes sure the search index exists before the indexer starts consuming.
/// </summary>
/// <remarks>
/// Runs in both the API and the worker, and is idempotent, because in a container-orchestrated
/// deployment there is no guarantee about which one starts first — or that either of them is the
/// one that survives the next restart. "Whoever gets there first creates it" is the only startup
/// ordering that does not need a coordinator.
/// </remarks>
public sealed partial class SearchIndexInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<SearchIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        try
        {
            await scope.ServiceProvider
                .GetRequiredService<ICommentSearchIndex>()
                .EnsureCreatedAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The worker must still start: messages should queue up rather than be rejected, and
            // the index can be created on a later attempt.
            LogFailed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not initialise the Elasticsearch index at startup; will rely on a later attempt")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>Traces and metrics for the worker process.</summary>
public static class WorkerTelemetryExtensions
{
    public static IServiceCollection AddWorkerTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("threadline-comments-worker"))
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddSource("MassTransit"))
            .WithMetrics(metrics => metrics
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 })
        {
            telemetry.UseOtlpExporter();
        }

        return services;
    }
}
