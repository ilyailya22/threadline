using System.Text.Json;
using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using HttpMethod = Elastic.Transport.HttpMethod;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Threadline.Comments.Infrastructure.Search;

/// <summary>
/// Elasticsearch read model for the top-level comment table.
/// </summary>
/// <remarks>
/// <para>
/// Index management and queries are issued through the low-level transport with explicit JSON
/// rather than the fluent object initialiser API. That is a deliberate trade: the mapping below is
/// the same JSON you would paste into Kibana, so it can be read, diffed and reasoned about by
/// anyone who knows Elasticsearch but not this client library — and it does not shift under a
/// client minor upgrade.
/// </para>
/// <para>
/// Sorting uses <c>keyword</c> sub-fields with a lowercase normaliser, so ordering by user name or
/// e-mail is a doc-values scan rather than an analysed-text comparison, and "Anonym" sorts next to
/// "anonym" the way a person expects.
/// </para>
/// </remarks>
public sealed partial class ElasticsearchCommentIndex(
    ElasticsearchClient client,
    IOptions<ElasticsearchOptions> options,
    ILogger<ElasticsearchCommentIndex> logger) : ICommentSearchIndex
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ElasticsearchOptions _options = options.Value;

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        var exists = await client.Transport.RequestAsync<StringResponse>(
            HttpMethod.HEAD,
            $"/{_options.IndexName}",
            cancellationToken: cancellationToken);

        if (exists.ApiCallDetails.HttpStatusCode == 200)
        {
            LogIndexReady(logger, _options.IndexName);
            return;
        }

        var body = PostData.String(BuildIndexDefinition(_options));

        var created = await client.Transport.RequestAsync<StringResponse>(
            HttpMethod.PUT,
            $"/{_options.IndexName}",
            body,
            cancellationToken: cancellationToken);

        if (created.ApiCallDetails.HttpStatusCode is not (200 or 201))
        {
            // resource_already_exists_exception is fine: two replicas started at once and one won.
            if (created.ApiCallDetails.HttpStatusCode == 400
                && created.Body?.Contains("resource_already_exists_exception", StringComparison.Ordinal) == true)
            {
                LogIndexReady(logger, _options.IndexName);
                return;
            }

            throw new InvalidOperationException(
                $"Could not create Elasticsearch index '{_options.IndexName}': {created.Body}");
        }

        LogIndexCreated(logger, _options.IndexName, _options.Alias);
    }

    public async Task<PagedResult<CommentListItemDto>> QueryTopLevelAsync(
        CommentPageRequest request,
        string? searchText = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = request.Normalized();

        object query = string.IsNullOrWhiteSpace(searchText)
            ? new { match_all = new { } }
            : new
            {
                multi_match = new
                {
                    query = searchText,
                    fields = new[] { "textPlain^2", "userName", "email" },
                    @operator = "and",
                },
            };

        var body = JsonSerializer.Serialize(
            new
            {
                from = page.Skip,
                size = page.PageSize,

                // Exact total: the table shows it, and a capped count ("10000") would be a lie the
                // user can see. For these queries Elasticsearch counts from index statistics, so the
                // exact figure is cheap; how far the user may page is limited separately.
                track_total_hits = true,
                query,
                sort = BuildSort(page),
            },
            Json);

        var response = await client.Transport.RequestAsync<StringResponse>(
            HttpMethod.POST,
            $"/{_options.Alias}/_search",
            PostData.String(body),
            cancellationToken: cancellationToken);

        if (response.ApiCallDetails.HttpStatusCode != 200)
        {
            throw new InvalidOperationException($"Elasticsearch search failed: {response.Body}");
        }

        return Parse(response.Body!, page);
    }

    /// <summary>
    /// Writes documents with external versioning, so a stale projection can never overwrite a
    /// fresher one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every document is a full projection rebuilt from SQL, and projections of the same thread can
    /// run concurrently on different workers and finish out of order. The version is the thread's
    /// reply count, which only ever grows, and <c>external_gte</c> makes Elasticsearch reject any
    /// write whose count is lower than what it already holds — so the last write to land is always
    /// at least as fresh as every one before it. Equal versions are accepted, which is what lets an
    /// attachment finishing processing update a thread whose count has not changed.
    /// </para>
    /// <para>
    /// <c>refresh=wait_for</c> returns only once the documents are searchable. The caller invalidates
    /// the list cache immediately afterwards; without the wait, the next reader would re-cache the
    /// page from an index that has not refreshed yet and serve it stale for the whole TTL.
    /// </para>
    /// </remarks>
    public async Task IndexManyAsync(
        IReadOnlyCollection<CommentSearchDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
        {
            return;
        }

        var ndjson = new System.Text.StringBuilder();

        foreach (var document in documents)
        {
            ndjson
                .Append(JsonSerializer.Serialize(
                    new
                    {
                        index = new
                        {
                            _id = document.Id.ToString("N"),
                            version = document.ReplyCount,
                            version_type = "external_gte",
                        },
                    },
                    Json))
                .Append('\n')
                .Append(JsonSerializer.Serialize(document, Json))
                .Append('\n');
        }

        var response = await client.Transport.RequestAsync<StringResponse>(
            HttpMethod.POST,
            $"/{_options.Alias}/_bulk?refresh=wait_for",
            PostData.String(ndjson.ToString()),
            cancellationToken: cancellationToken);

        if (response.ApiCallDetails.HttpStatusCode != 200)
        {
            throw new InvalidOperationException($"Elasticsearch bulk index failed: {response.Body}");
        }

        EnsureNoRealFailures(response.Body);
    }

    /// <summary>
    /// A version conflict on a bulk item means a fresher projection already landed — the intended
    /// outcome, not an error. Anything else is a genuine failure and must be retried.
    /// </summary>
    private static void EnsureNoRealFailures(string? body)
    {
        if (body is null || !body.Contains("\"errors\":true", StringComparison.Ordinal))
        {
            return;
        }

        using var parsed = JsonDocument.Parse(body);

        foreach (var item in parsed.RootElement.GetProperty("items").EnumerateArray())
        {
            var result = item.GetProperty("index");

            if (result.TryGetProperty("error", out var error)
                && error.GetProperty("type").GetString() != "version_conflict_engine_exception")
            {
                throw new InvalidOperationException($"Elasticsearch bulk index failed: {error}");
            }
        }
    }

    private static object[] BuildSort(CommentPageRequest page)
    {
        var direction = page.Direction == SortDirection.Ascending ? "asc" : "desc";

        // The secondary sort on id is not cosmetic: without a deterministic tie-break, two documents
        // that compare equal can swap between page 1 and page 2, and the user sees one comment twice
        // and another not at all. Ids are UUID v7, so "desc by id" is also "newest first".
        return page.SortBy switch
        {
            CommentSortField.UserName =>
            [
                Order("userName.sort", direction),
                Order("id", "desc"),
            ],
            CommentSortField.Email =>
            [
                Order("email.sort", direction),
                Order("id", "desc"),
            ],
            _ =>
            [
                Order("createdAt", direction),
                Order("id", direction),
            ],
        };

        static Dictionary<string, object> Order(string field, string direction) =>
            new(StringComparer.Ordinal) { [field] = new { order = direction } };
    }

    private static PagedResult<CommentListItemDto> Parse(string json, CommentPageRequest page)
    {
        using var document = JsonDocument.Parse(json);

        var hits = document.RootElement.GetProperty("hits");
        var total = hits.GetProperty("total").GetProperty("value").GetInt64();

        var items = new List<CommentListItemDto>(page.PageSize);

        foreach (var hit in hits.GetProperty("hits").EnumerateArray())
        {
            var source = hit.GetProperty("_source").Deserialize<CommentSearchDocument>(Json);

            if (source is null)
            {
                continue;
            }

            items.Add(new CommentListItemDto(
                source.Id,
                new AuthorDto(source.AuthorId, source.UserName, source.Email, source.HomePage),
                source.TextHtml,
                CommentBody.ToPreview(source.TextPlain),
                source.CreatedAt,
                source.ReplyCount,
                source.LastReplyAt,
                source.Attachments));
        }

        return new PagedResult<CommentListItemDto>(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// The index definition, as plain JSON.
    /// </summary>
    private static string BuildIndexDefinition(ElasticsearchOptions options) =>
        $$"""
        {
          "settings": {
            "number_of_shards": {{options.NumberOfShards}},
            "number_of_replicas": {{options.NumberOfReplicas}},
            "refresh_interval": "1s",
            "analysis": {
              "normalizer": {
                "lowercase_normalizer": { "type": "custom", "filter": ["lowercase", "asciifolding"] }
              }
            }
          },
          "aliases": { "{{options.Alias}}": {} },
          "mappings": {
            "dynamic": "strict",
            "properties": {
              "id":        { "type": "keyword" },
              "authorId":  { "type": "keyword" },
              "userName":  {
                "type": "text",
                "fields": { "sort": { "type": "keyword", "normalizer": "lowercase_normalizer" } }
              },
              "email":     {
                "type": "keyword",
                "fields": { "sort": { "type": "keyword", "normalizer": "lowercase_normalizer" } }
              },
              "homePage":  { "type": "keyword", "index": false },
              "textHtml":  { "type": "text", "index": false },
              "textPlain": { "type": "text" },
              "createdAt": { "type": "date" },
              "replyCount": { "type": "integer" },
              "lastReplyAt": { "type": "date" },
              "attachments": { "type": "object", "enabled": false }
            }
          }
        }
        """;

    [LoggerMessage(Level = LogLevel.Information, Message = "Elasticsearch index {Index} already exists")]
    private static partial void LogIndexReady(ILogger logger, string index);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created Elasticsearch index {Index} with alias {Alias}")]
    private static partial void LogIndexCreated(ILogger logger, string index, string alias);
}
