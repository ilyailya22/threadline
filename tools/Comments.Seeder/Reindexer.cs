using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.Infrastructure.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Threadline.Comments.Seeder;

/// <summary>
/// Rebuilds the Elasticsearch read model from SQL Server.
/// </summary>
/// <remarks>
/// Uses the same <see cref="CommentSearchProjector"/> the live indexer uses, so a reindexed document
/// is byte-for-byte what the event-driven path would have produced. Walks the thread roots by keyset
/// (id greater than the last one seen) rather than by offset, so the cost of each batch stays flat
/// all the way to the end of a million-row table.
/// </remarks>
internal static class Reindexer
{
    private const int BatchSize = 2_000;

    public static async Task<int> RunAsync(string connectionString, IConfiguration configuration)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(configuration);
        services.Configure<ElasticsearchOptions>(configuration.GetSection(ElasticsearchOptions.SectionName));
        services.AddSingleton<IDateTimeProvider, Infrastructure.Common.SystemDateTimeProvider>();
        services.AddSingleton<IAttachmentUrlBuilder, Infrastructure.Storage.AttachmentUrlBuilder>();
        services.AddDbContext<AppDbContext>(options => options
            .UseSqlServer(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddSearch();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var index = scope.ServiceProvider.GetRequiredService<ICommentSearchIndex>();
        var projector = scope.ServiceProvider.GetRequiredService<CommentSearchProjector>();

        await index.EnsureCreatedAsync();

        var total = await context.Comments.CountAsync(c => c.ParentId == null);
        AnsiConsole.MarkupLine($"Reindexing [yellow]{total:N0}[/] threads…");

        var done = 0;
        Guid? after = null;

        while (true)
        {
            var ids = await context.Comments
                .Where(c => c.ParentId == null && (after == null || c.Id.CompareTo(after.Value) > 0))
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (ids.Count == 0)
            {
                break;
            }

            var documents = await projector.BuildAsync(ids);
            await index.IndexManyAsync([.. documents]);

            done += ids.Count;
            after = ids[^1];
            context.ChangeTracker.Clear();

            AnsiConsole.MarkupLine($"  {done:N0} / {total:N0}");
        }

        AnsiConsole.MarkupLine("[green]Reindex complete.[/]");
        return 0;
    }
}
