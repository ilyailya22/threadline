using Threadline.Comments.Infrastructure;
using Threadline.Comments.Infrastructure.Messaging;
using Threadline.Comments.Infrastructure.Observability;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "threadline-comments-worker"));

// No AddApplication(): the worker runs no MediatR requests. Registering the HTTP command handlers
// here pulled in dependencies only the web tier provides (IClientContext), which the Development
// host rejects at start-up when it validates the container.
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddInfrastructureInitializer(applyMigrations: false);

// Everything the request path refuses to do lives here: draining the outbox onto the broker,
// keeping Elasticsearch in step, and turning uploads into 320x240 images. Scaling this deployment
// scales throughput without touching the web tier.
builder.Services.AddOutboxPublisher();
builder.Services.AddMessaging(builder.Configuration, bus => bus.AddBackgroundConsumers());

builder.Services.AddCommentsTelemetry(builder.Configuration, "threadline-comments-worker");

await builder.Build().RunAsync();
