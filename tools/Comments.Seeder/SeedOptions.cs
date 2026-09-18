namespace Threadline.Comments.Seeder;

/// <summary>What to generate. Defaults are the numbers the assignment names.</summary>
public sealed record SeedOptions
{
    /// <summary>"У нас 1 000 000 сообщений".</summary>
    public int Comments { get; init; } = 1_000_000;

    /// <summary>"100к пользователей в 24 час".</summary>
    public int Users { get; init; } = 100_000;

    /// <summary>
    /// Share of comments that are top-level rather than replies.
    /// </summary>
    /// <remarks>
    /// 4% matches how real discussion boards distribute: a minority of threads, most of the volume
    /// in replies. It also matters for the benchmark — the filtered index on top-level comments is
    /// only interesting when most rows are <em>not</em> in it.
    /// </remarks>
    public double TopLevelRatio { get; init; } = 0.04;

    /// <summary>Maximum reply depth to generate.</summary>
    public int MaxDepth { get; init; } = 8;

    /// <summary>Rows per SqlBulkCopy batch.</summary>
    public int BatchSize { get; init; } = 20_000;

    /// <summary>Spread the generated timestamps over this many days.</summary>
    public int SpreadDays { get; init; } = 90;

    /// <summary>Fixed seed so two runs produce the same data and two benchmarks are comparable.</summary>
    public int RandomSeed { get; init; } = 20260917;

    /// <summary>Also push the generated top-level comments into Elasticsearch.</summary>
    public bool IndexSearch { get; init; } = true;

    /// <summary>Delete existing data before generating.</summary>
    public bool Truncate { get; init; }
}
