using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Threadline.Comments.Infrastructure.Observability;

public static class TelemetryExtensions
{
    public const string AzureMonitorConnectionStringKey = "ApplicationInsights:ConnectionString";

    /// <summary>
    /// Traces and metrics common to every process: outgoing HTTP (Elasticsearch, Blob Storage),
    /// MassTransit and the runtime, exported over OTLP when an endpoint is configured.
    /// </summary>
    /// <remarks>
    /// Hosts add what is specific to them through the returned builder — the API its request
    /// instrumentation and Azure Monitor export.
    /// </remarks>
    public static OpenTelemetryBuilder AddCommentsTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddSource("MassTransit"))
            .WithMetrics(metrics => metrics
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        // Azure Monitor, where configured, is the exporter; OTLP is for whatever runs locally
        // (Aspire dashboard, Jaeger, Grafana).
        if (!UsesAzureMonitor(configuration) && configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 })
        {
            telemetry.UseOtlpExporter();
        }

        return telemetry;
    }

    public static bool UsesAzureMonitor(IConfiguration configuration) =>
        configuration?[AzureMonitorConnectionStringKey] is { Length: > 0 };
}
