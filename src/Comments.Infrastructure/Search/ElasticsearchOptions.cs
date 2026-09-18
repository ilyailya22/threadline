namespace Threadline.Comments.Infrastructure.Search;

public sealed class ElasticsearchOptions
{
    public const string SectionName = "Elasticsearch";

    public string Url { get; set; } = "http://localhost:9200";

    public string? Username { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// Alias the application reads and writes. It points at a concrete, versioned index
    /// (<c>comments-v1</c>, <c>comments-v2</c>, …) so a mapping change can be rolled out by building
    /// the new index in the background and repointing the alias atomically — no downtime and no
    /// "search is empty while we reindex" window.
    /// </summary>
    public string Alias { get; set; } = "comments";

    public string IndexName { get; set; } = "comments-v1";

    public int NumberOfShards { get; set; } = 1;

    public int NumberOfReplicas { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
