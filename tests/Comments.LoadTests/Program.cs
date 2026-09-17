using System.Net.Http.Json;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using NBomber.Http.CSharp;

// NBomber counterpart to the k6 scripts.
//
// Both exist on purpose, and they are not redundant. k6 is what you reach for when tuning: it runs
// from any machine, its output is what load-testing tools are expected to produce, and it does not
// need the .NET SDK. NBomber lives inside the solution, so it can be run from `dotnet run` in CI
// with no extra toolchain and its report is a build artefact like any other.
//
//   dotnet run --project tests/Comments.LoadTests -- --url http://localhost:5080 --duration 120

var baseUrl = GetArgument(args, "--url") ?? "http://localhost:5080";
var duration = TimeSpan.FromSeconds(int.TryParse(GetArgument(args, "--duration"), out var s) ? s : 120);
var rate = int.TryParse(GetArgument(args, "--rate"), out var r) ? r : 200;

Console.WriteLine($"Target: {baseUrl}   rate: {rate} req/s   duration: {duration}");

using var httpClient = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(30),
};

var random = new Random(20260917);

// The default LIFO first page: the single hottest request on the site, and the one the cache is
// supposed to absorb. If this scenario degrades, the cache is not doing its job.
var firstPage = Scenario.Create("first_page_lifo", async _ =>
    {
        var request = Http.CreateRequest("GET", "/api/comments?page=1&pageSize=25&sortBy=createdAt&direction=descending")
            .WithHeader("Accept", "application/json");

        return await Http.Send(httpClient, request);
    })
    .WithoutWarmUp()
    .WithLoadSimulations(Simulation.Inject(rate: (int)(rate * 0.6), interval: TimeSpan.FromSeconds(1), during: duration));

// Sorting by e-mail with a deep offset: the query the cache cannot help with and SQL would struggle
// with. This is the scenario that justifies having Elasticsearch at all.
var deepSort = Scenario.Create("deep_page_sorted_by_email", async _ =>
    {
        var page = random.Next(50, 300);

        var request = Http.CreateRequest("GET", $"/api/comments?page={page}&pageSize=25&sortBy=email&direction=ascending")
            .WithHeader("Accept", "application/json");

        return await Http.Send(httpClient, request);
    })
    .WithoutWarmUp()
    .WithLoadSimulations(Simulation.Inject(rate: (int)(rate * 0.25), interval: TimeSpan.FromSeconds(1), during: duration));

// Loading a thread: the materialised-path range scan.
var thread = Scenario.Create("thread", async context =>
    {
        var listResponse = await httpClient.GetFromJsonAsync<PagedResponse>(
            $"/api/comments?page={random.Next(1, 20)}&pageSize=25");

        if (listResponse?.Items is not { Count: > 0 } items)
        {
            return Response.Fail(statusCode: "204", message: "no comments to open");
        }

        var id = items[random.Next(items.Count)].Id;
        var request = Http.CreateRequest("GET", $"/api/comments/{id}/thread?maxDepth=10");

        return await Http.Send(httpClient, request);
    })
    .WithoutWarmUp()
    .WithLoadSimulations(Simulation.Inject(rate: (int)(rate * 0.15), interval: TimeSpan.FromSeconds(1), during: duration));

var stats = NBomberRunner
    .RegisterScenarios(firstPage, deepSort, thread)
    .WithReportFolder("loadtests/results/nbomber")
    .WithReportFormats(ReportFormat.Html, ReportFormat.Md, ReportFormat.Csv)
    .WithTestSuite("threadline-comments")
    .WithTestName("read-path")
    .Run();

// Assert the same SLOs the k6 thresholds check, so this can fail a pipeline rather than just print
// a report nobody reads.
var failed = false;

foreach (var scenario in stats.ScenarioStats)
{
    var p95 = scenario.Ok.Latency.Percent95;
    var total = scenario.Ok.Request.Count + scenario.Fail.Request.Count;
    var errorRate = scenario.Fail.Request.Count / (double)Math.Max(1, total);

    Console.WriteLine($"{scenario.ScenarioName}: p95 {p95:F0} ms, errors {errorRate:P2}");

    if (p95 > 500 || errorRate > 0.01)
    {
        Console.Error.WriteLine($"  SLO breached for {scenario.ScenarioName}");
        failed = true;
    }
}

return failed ? 1 : 0;

static string? GetArgument(string[] args, string name)
{
    var index = Array.IndexOf(args, name);

    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

internal sealed record PagedResponse(List<ListItem> Items);

internal sealed record ListItem(Guid Id);
