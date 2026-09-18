using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure;

/// <summary>
/// One-time startup work: apply migrations, create the search index and the blob container.
/// </summary>
/// <remarks>
/// <para>
/// Runs in both the API and the worker, and every step is idempotent, because in a
/// container-orchestrated deployment there is no guarantee about which one starts first. "Whoever
/// gets there first creates it" is the only startup ordering that does not need a coordinator.
/// </para>
/// <para>
/// Migrations are the exception: they run only where <c>applyMigrations</c> is set (the API), and a
/// failure stops the host, because serving against the wrong schema is worse than not serving.
/// Running them from the application is a deliberate choice for a system deployed as one unit —
/// <c>docker compose up</c> comes up working with no manual step. In a multi-team system this
/// belongs in a release pipeline step instead; see docs/IMPROVEMENTS.md.
/// </para>
/// <para>
/// The search index and the blob container are not fatal: the list falls back to SQL, and the
/// container can be created on a later start. The process must still come up so that messages
/// queue rather than being rejected.
/// </para>
/// </remarks>
public sealed partial class InfrastructureInitializer(
    IServiceScopeFactory scopeFactory,
    bool applyMigrations,
    ILogger<InfrastructureInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        if (applyMigrations)
        {
            await services.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);
            LogSchemaReady(logger);
        }

        await TryAsync(
            "Elasticsearch index",
            token => services.GetRequiredService<ICommentSearchIndex>().EnsureCreatedAsync(token),
            cancellationToken);

        await TryAsync(
            "blob container",
            token => services.GetRequiredService<BlobFileStorage>().EnsureCreatedAsync(token),
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task TryAsync(string resource, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogInitializationFailed(logger, exception, resource);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database schema is up to date")]
    private static partial void LogSchemaReady(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not initialise the {Resource} at startup; will rely on a later attempt")]
    private static partial void LogInitializationFailed(ILogger logger, Exception exception, string resource);
}
