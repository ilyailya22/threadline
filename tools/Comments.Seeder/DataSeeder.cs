using System.Data;
using System.Diagnostics;
using System.Globalization;
using Bogus;
using Threadline.Comments.Domain.Comments;
using Microsoft.Data.SqlClient;
using Spectre.Console;

namespace Threadline.Comments.Seeder;

/// <summary>
/// Generates the million-comment dataset the Middle+ load test runs against.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not EF Core.</b> A million inserts through the change tracker is hours of work and
/// gigabytes of tracked entities. <see cref="SqlBulkCopy"/> writes the same rows in minutes,
/// because it is the same mechanism <c>bcp</c> uses: minimally logged, batched, no per-row round
/// trip. The domain model still decides <em>what</em> the rows contain — paths, ids and depths come
/// from the same <see cref="CommentPath"/> logic the application uses, so the seeded data is
/// indistinguishable from data the API produced.
/// </para>
/// <para>
/// <b>Why the shape matters.</b> A benchmark against a million identical top-level comments proves
/// nothing: the filtered index would cover every row and the thread queries would never touch more
/// than one level. The generator mirrors a real board — a minority of threads carrying most of the
/// replies, a long tail of one-reply threads, timestamps spread over months — so the indexes are
/// exercised the way production would exercise them.
/// </para>
/// </remarks>
public sealed class DataSeeder(string connectionString)
{
    private const string Pepper = "seeded";

    public async Task RunAsync(SeedOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();

        if (options.Truncate)
        {
            await TruncateAsync(cancellationToken);
        }

        var random = new Random(options.RandomSeed);
        Randomizer.Seed = new Random(options.RandomSeed);

        var users = await SeedUsersAsync(options, cancellationToken);
        var roots = await SeedCommentsAsync(options, users, random, cancellationToken);

        stopwatch.Stop();

        AnsiConsole.MarkupLine(
            $"[green]Done[/] in [yellow]{stopwatch.Elapsed:hh\\:mm\\:ss}[/] — "
            + $"{options.Users:N0} users, {options.Comments:N0} comments, {roots.Count:N0} threads");
    }

    private async Task TruncateAsync(CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[yellow]Truncating existing data…[/]");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Order matters: children before parents, and the self-referencing FK on Comments means the
        // whole table has to go at once rather than row by row.
        foreach (var statement in new[]
                 {
                     "DELETE FROM [Attachments]",
                     "DELETE FROM [OutboxMessages]",
                     "DELETE FROM [InboxMessages]",
                     "DELETE FROM [Comments]",
                     "DELETE FROM [Users]",
                 })
        {
            await using var command = new SqlCommand(statement, connection) { CommandTimeout = 600 };
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<List<SeededUser>> SeedUsersAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        var faker = new Faker("en");
        var users = new List<SeededUser>(options.Users);
        var baseTime = DateTimeOffset.UtcNow.AddDays(-options.SpreadDays);

        var table = CreateUsersTable();

        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async context =>
            {
                var task = context.AddTask("[blue]Users[/]", maxValue: options.Users);

                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                for (var i = 0; i < options.Users; i++)
                {
                    // Latin letters and digits only — the same rule UserName.Create enforces, so the
                    // seeded rows would survive a round trip through the API's validation.
                    var name = $"{faker.Internet.UserName().Replace(".", string.Empty, StringComparison.Ordinal)}{i}";
                    name = new string([.. name.Where(char.IsLetterOrDigit)]);
                    name = name.Length > 64 ? name[..64] : name;

                    var createdAt = baseTime.AddSeconds(i % (options.SpreadDays * 86_400));
                    var id = Guid.CreateVersion7(createdAt);
                    var email = $"user{i}@{faker.Internet.DomainName()}";

                    users.Add(new SeededUser(id, name, email));

                    table.Rows.Add(
                        id,
                        name,
                        email,
                        i % 5 == 0 ? $"https://{faker.Internet.DomainName()}" : DBNull.Value,
                        createdAt,
                        createdAt);

                    if (table.Rows.Count >= options.BatchSize)
                    {
                        await BulkCopyAsync(connection, table, "Users", cancellationToken);
                        task.Increment(table.Rows.Count);
                        table.Clear();
                    }
                }

                if (table.Rows.Count > 0)
                {
                    await BulkCopyAsync(connection, table, "Users", cancellationToken);
                    task.Increment(table.Rows.Count);
                }

                task.Value = task.MaxValue;
            });

        return users;
    }

    private async Task<List<SeededComment>> SeedCommentsAsync(
        SeedOptions options,
        List<SeededUser> users,
        Random random,
        CancellationToken cancellationToken)
    {
        var faker = new Faker("en");
        var roots = new List<SeededComment>();

        // Only the most recent slice of threads is kept as reply targets. Holding a million
        // candidates would cost hundreds of megabytes for no benefit — and on a real board, replies
        // cluster on recent threads anyway, which is the access pattern worth reproducing.
        var replyTargets = new List<SeededComment>(capacity: 50_000);

        var baseTime = DateTimeOffset.UtcNow.AddDays(-options.SpreadDays);
        var secondsSpan = options.SpreadDays * 86_400L;

        var table = CreateCommentsTable();

        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .StartAsync(async context =>
            {
                var task = context.AddTask("[blue]Comments[/]", maxValue: options.Comments);

                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                for (var i = 0; i < options.Comments; i++)
                {
                    var createdAt = baseTime.AddSeconds((long)(i / (double)options.Comments * secondsSpan));
                    var id = Guid.CreateVersion7(createdAt);
                    var author = users[random.Next(users.Count)];

                    var makeRoot = replyTargets.Count == 0 || random.NextDouble() < options.TopLevelRatio;

                    SeededComment comment;

                    if (makeRoot)
                    {
                        var path = CommentPath.ForRoot(id);
                        comment = new SeededComment(id, null, id, path, 1, author, createdAt);
                        roots.Add(comment);
                    }
                    else
                    {
                        // Bias towards recent threads: a fresh thread is far more likely to get the
                        // next reply than one from two months ago.
                        var index = replyTargets.Count - 1 - (int)(Math.Pow(random.NextDouble(), 2) * (replyTargets.Count - 1));
                        var parent = replyTargets[index];

                        if (parent.Depth >= options.MaxDepth)
                        {
                            parent = roots[^Math.Min(roots.Count, random.Next(1, 200))];
                        }

                        var path = CommentPath.ForReply(parent.Path, id);
                        comment = new SeededComment(id, parent.Id, parent.RootId, path, path.Depth, author, createdAt);
                    }

                    replyTargets.Add(comment);

                    if (replyTargets.Count > 50_000)
                    {
                        replyTargets.RemoveRange(0, 25_000);
                    }

                    var plain = faker.Lorem.Sentences(random.Next(1, 4));
                    var html = Decorate(plain, random);

                    table.Rows.Add(
                        comment.Id,
                        author.Id,
                        comment.ParentId is { } parentId ? parentId : DBNull.Value,
                        comment.RootId,
                        comment.Path.Value,
                        comment.Depth,
                        html,
                        plain,
                        createdAt,
                        HashIp(random.Next(1, 40_000)),
                        "Mozilla/5.0 (seeded)",
                        DBNull.Value);

                    if (table.Rows.Count >= options.BatchSize)
                    {
                        await BulkCopyAsync(connection, table, "Comments", cancellationToken);
                        task.Increment(table.Rows.Count);
                        table.Clear();
                    }
                }

                if (table.Rows.Count > 0)
                {
                    await BulkCopyAsync(connection, table, "Comments", cancellationToken);
                    task.Increment(table.Rows.Count);
                }

                task.Value = task.MaxValue;
            });

        return roots;
    }

    /// <summary>Sprinkles in the allowed tags so the rendered output is not uniformly plain text.</summary>
    private static string Decorate(string text, Random random) =>
        random.Next(4) switch
        {
            0 => $"<strong>{Escape(text)}</strong>",
            1 => $"{Escape(text)} <i>note</i>",
            2 => $"<code>{Escape(text)}</code>",
            _ => Escape(text),
        };

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    private static string HashIp(int seed) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"{Pepper}:{seed}")));

    private static async Task BulkCopyAsync(
        SqlConnection connection,
        DataTable table,
        string destination,
        CancellationToken cancellationToken)
    {
        using var bulk = new SqlBulkCopy(connection)
        {
            DestinationTableName = destination,
            BatchSize = table.Rows.Count,
            BulkCopyTimeout = 600,
            EnableStreaming = true,
        };

        foreach (DataColumn column in table.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await bulk.WriteToServerAsync(table, cancellationToken);
    }

    private static DataTable CreateUsersTable()
    {
        var table = new DataTable("Users") { Locale = CultureInfo.InvariantCulture };

        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("UserName", typeof(string));
        table.Columns.Add("Email", typeof(string));
        table.Columns.Add("HomePage", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTimeOffset));
        table.Columns.Add("LastPostedAt", typeof(DateTimeOffset));

        return table;
    }

    private static DataTable CreateCommentsTable()
    {
        var table = new DataTable("Comments") { Locale = CultureInfo.InvariantCulture };

        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("AuthorId", typeof(Guid));
        table.Columns.Add("ParentId", typeof(Guid));
        table.Columns.Add("RootId", typeof(Guid));
        table.Columns.Add("Path", typeof(string));
        table.Columns.Add("Depth", typeof(int));
        table.Columns.Add("TextHtml", typeof(string));
        table.Columns.Add("TextPlain", typeof(string));
        table.Columns.Add("CreatedAt", typeof(DateTimeOffset));
        table.Columns.Add("ClientIpHash", typeof(string));
        table.Columns.Add("ClientUserAgent", typeof(string));
        table.Columns.Add("ClientId", typeof(Guid));

        return table;
    }

    private sealed record SeededUser(Guid Id, string UserName, string Email);

    private sealed record SeededComment(
        Guid Id,
        Guid? ParentId,
        Guid RootId,
        CommentPath Path,
        int Depth,
        SeededUser Author,
        DateTimeOffset CreatedAt);
}
