using System.Data.Common;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Common.Abstractions;

using Threadline.Comments.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Respawn;
using Testcontainers.Azurite;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Threadline.Comments.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real application against real engines.
/// </summary>
/// <remarks>
/// <para>
/// Every dependency runs in a container: SQL Server, Redis, RabbitMQ, Elasticsearch and Azurite.
/// Nothing is mocked, because the things most likely to be wrong are precisely the things a mock
/// cannot catch — an EF value conversion that does not translate, a filtered index that the query
/// planner ignores, an Elasticsearch sort on a field that was mapped as analysed text, an
/// <c>UPDLOCK, READPAST</c> hint that behaves differently than expected under concurrency.
/// </para>
/// <para>
/// The containers are started once for the whole run and the database is reset between tests with
/// Respawn, which deletes rows in dependency order and is far faster than recreating a schema.
/// </para>
/// <para>
/// The only production behaviour changed here is the CAPTCHA, which a test cannot solve by
/// definition. It is switched to the load-test bypass — the same decorator the write benchmark
/// uses — so everything else on the write path is exercised exactly as it ships.
/// </para>
/// </remarks>
public sealed class CommentsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>The answer that satisfies any challenge while the bypass is on.</summary>
    public const string CaptchaAnswer = "TESTBYPASS";

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine")
        .Build();

    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:3-management-alpine")
        .Build();

    // A generic container rather than the Elasticsearch module: the module assumes security is on,
    // waits for an authenticated HTTPS endpoint and hands out an https:// URL with credentials. With
    // security off — as in docker-compose — that wait never completes.
    private readonly IContainer _elastic = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:9.1.5")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .WithPortBinding(9200, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9200).ForPath("/_cluster/health")))
        .Build();

    private readonly AzuriteContainer _azurite = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
        .Build();

    private Respawner? _respawner;
    private DbConnection? _resetConnection;

    public async Task InitializeAsync()
    {
        // In parallel: five containers started one after another is several minutes of waiting for
        // no reason, since none of them depends on another.
        await Task.WhenAll(
            _sql.StartAsync(),
            _redis.StartAsync(),
            _rabbit.StartAsync(),
            _elastic.StartAsync(),
            _azurite.StartAsync());

        // Touching Services forces the host to build, which applies the migrations.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _resetConnection = new Microsoft.Data.SqlClient.SqlConnection(_sql.GetConnectionString());
        await _resetConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_resetConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,

            // The migration history must survive, or every reset would leave a schema-less database.
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
    }

    /// <summary>
    /// Returns every store to empty between tests: SQL rows, search documents and cached pages.
    /// Resetting SQL alone is not enough — the list is served from Elasticsearch through Redis, so
    /// documents and cached pages from one test would otherwise leak into the next.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        // Let the previous test's pipeline drain first. Its events may still be on their way through
        // RabbitMQ to the indexer; a projection that read SQL before the reset and writes to
        // Elasticsearch after it would leak a document into the next test.
        await WaitForPipelineToDrainAsync();

        if (_respawner is not null && _resetConnection is not null)
        {
            await _respawner.ResetAsync(_resetConnection);
        }

        using var http = new HttpClient
        {
            BaseAddress = new Uri($"http://{_elastic.Hostname}:{_elastic.GetMappedPublicPort(9200)}"),
        };

        using var body = new StringContent(
            """{"query":{"match_all":{}}}""",
            System.Text.Encoding.UTF8,
            "application/json");

        // 404 simply means the index has not been created yet — nothing to clear.
        await http.PostAsync("/_all/_delete_by_query?refresh=true&conflicts=proceed", body);

        // The application's own invalidation, rather than FLUSHALL: it also clears the in-process L1
        // tier, which a Redis command cannot reach.
        await Services.GetRequiredService<ICommentCache>().InvalidateTopLevelAsync();
    }

    private async Task WaitForPipelineToDrainAsync()
    {
        if (_resetConnection is null)
        {
            return;
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            await using var command = _resetConnection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM OutboxMessages WHERE ProcessedAt IS NULL";

            if (Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) == 0)
            {
                break;
            }

            await Task.Delay(100);
        }

        // Published is not consumed: give in-flight consumers time to finish their writes. The
        // indexer batches for up to 100 ms and then waits for an index refresh (up to 1 s).
        await Task.Delay(1_500);
    }

    /// <summary>Drops cached list pages, so the next read goes to the search index.</summary>
    public Task ResetCacheAsync() => Services.GetRequiredService<ICommentCache>().InvalidateTopLevelAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");

        // UseSetting rather than ConfigureAppConfiguration: Program.cs reads connection strings while
        // it is still composing services, before app-configuration callbacks from the factory have
        // run. Values supplied that way arrive too late, and the API silently falls back to
        // appsettings.json — which points at localhost, i.e. at whatever stack happens to be running.
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:SqlServer"] = _sql.GetConnectionString(),
            ["ConnectionStrings:Redis"] = _redis.GetConnectionString(),
            ["ConnectionStrings:RabbitMq"] = _rabbit.GetConnectionString(),
            ["Elasticsearch:Url"] = $"http://{_elastic.Hostname}:{_elastic.GetMappedPublicPort(9200)}",

            // A unique index per test run, so a leftover index from a previous run cannot make
            // a test pass or fail for the wrong reason.
            ["Elasticsearch:IndexName"] = $"comments-test-{Guid.NewGuid():N}",
            ["Elasticsearch:Alias"] = $"comments-test-alias-{Guid.NewGuid():N}",
            ["Storage:ConnectionString"] = _azurite.GetConnectionString(),
            ["Storage:ContainerName"] = "attachments",
            ["Privacy:IpHashPepper"] = "integration-test-pepper",
            [$"{LoadTestOptions.SectionName}:Enabled"] = "true",
            [$"{LoadTestOptions.SectionName}:BypassAnswer"] = CaptchaAnswer,
            ["Serilog:MinimumLevel:Default"] = "Warning",

            // One process runs the whole pipeline: outbox → RabbitMQ → indexer → Elasticsearch.
            ["Messaging:HostWorkerConsumers"] = "true",

            // Tests post dozens of comments in seconds from one client; production limits would
            // (correctly) reject that. The limiter itself is covered by its own configuration.
            ["RateLimiting:WritePerMinute"] = "10000",
            ["RateLimiting:CaptchaPerMinute"] = "10000",
            ["RateLimiting:ReadPerMinute"] = "100000",
            ["Outbox:PollInterval"] = "00:00:00.100",
        };

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>
    /// Explicit, because <see cref="WebApplicationFactory{TEntryPoint}"/> already has a
    /// <c>DisposeAsync</c> returning <see cref="ValueTask"/> and xUnit's lifetime interface wants a
    /// <see cref="Task"/>. Both end up here.
    /// </summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_resetConnection is not null)
        {
            await _resetConnection.DisposeAsync();
        }

        await base.DisposeAsync();

        await Task.WhenAll(
            _sql.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask(),
            _rabbit.DisposeAsync().AsTask(),
            _elastic.DisposeAsync().AsTask(),
            _azurite.DisposeAsync().AsTask());
    }
}

/// <summary>
/// Shares one set of containers across every test class.
/// </summary>
/// <remarks>
/// Starting SQL Server and Elasticsearch takes the best part of a minute. Paying that once per run
/// rather than once per class is the difference between a suite that gets run and one that does not.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class IntegrationTestSuite : ICollectionFixture<CommentsApiFactory>
{
    public const string Name = "integration";
}
