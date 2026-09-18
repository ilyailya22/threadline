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

// --reindex rebuilds the search index from SQL instead of generating data. It is the recovery path
// for a lost or corrupted index, and for messages that exhausted their retries and landed in an
// _error queue: SQL is the source of truth, so the index can always be derived from it again.
if (configuration.GetValue("reindex", false))
{
    return await Reindexer.RunAsync(
        connectionString,
        configuration,
        recreate: configuration.GetValue("recreate", false));
}

var seeder = new DataSeeder(connectionString);

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    await seeder.RunAsync(options, cancellation.Token);

    // The search index is built by the same projector the live pipeline uses, so seeded documents
    // carry real text and true reply counts — a benchmark against placeholder documents would
    // measure a different index from the one production serves.
    return options.IndexSearch
        ? await Reindexer.RunAsync(connectionString, configuration, recreate: options.Truncate)
        : 0;
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
