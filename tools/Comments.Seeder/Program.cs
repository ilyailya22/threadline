using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Infrastructure;
using Threadline.Comments.Seeder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

// Generates the dataset the Middle+ load test runs against:
//
//     dotnet run --project tools/Comments.Seeder -- --comments 1000000 --users 100000 --truncate
//
// Defaults are the numbers from the assignment, so `dotnet run` with no arguments produces exactly
// the scenario it describes.

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var options = new SeedOptions
{
    Comments = configuration.GetValue("comments", 1_000_000),
    Users = configuration.GetValue("users", 100_000),
    BatchSize = configuration.GetValue("batchSize", 20_000),
    MaxDepth = configuration.GetValue("maxDepth", 8),
    SpreadDays = configuration.GetValue("spreadDays", 90),
    IndexSearch = configuration.GetValue("indexSearch", true),
    Truncate = configuration.GetValue("truncate", false),
};

var connectionString = configuration.GetConnectionString("SqlServer")
    ?? "Server=localhost,1433;Database=ThreadlineComments;User Id=sa;Password=Your_strong_Passw0rd;TrustServerCertificate=True;Encrypt=False";

AnsiConsole.Write(new Rule("[bold]Threadline Comments — data seeder[/]").LeftJustified());
AnsiConsole.MarkupLine($"Target      : [yellow]{options.Comments:N0}[/] comments, [yellow]{options.Users:N0}[/] users");
AnsiConsole.MarkupLine($"Top-level   : [yellow]{options.TopLevelRatio:P0}[/] (≈ {options.Comments * options.TopLevelRatio:N0} threads)");
AnsiConsole.MarkupLine($"Search index: [yellow]{(options.IndexSearch ? "yes" : "no")}[/]");
AnsiConsole.WriteLine();

ICommentSearchIndex? searchIndex = null;

if (options.IndexSearch)
{
    var services = new ServiceCollection();

    services.AddLogging(logging => logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddSingleton<IConfiguration>(configuration);
    services.Configure<Threadline.Comments.Infrastructure.Search.ElasticsearchOptions>(
        configuration.GetSection(Threadline.Comments.Infrastructure.Search.ElasticsearchOptions.SectionName));
    services.AddSearch();

    searchIndex = services.BuildServiceProvider().GetRequiredService<ICommentSearchIndex>();
}

var seeder = new DataSeeder(connectionString, searchIndex);

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    await seeder.RunAsync(options, cancellation.Token);
    return 0;
}
catch (OperationCanceledException)
{
    AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
    return 130;
}
catch (Exception exception)
{
    AnsiConsole.MarkupLine($"[red]Failed:[/] {exception.Message}");
    return 1;
}
