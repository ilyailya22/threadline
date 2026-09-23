using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Infrastructure.Persistence;
using Threadline.Comments.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Threadline.Comments.IntegrationTests;

/// <summary>
/// What happens when the projection stops while everything else keeps working.
/// </summary>
/// <remarks>
/// The list is served from Elasticsearch. If the cluster falls over, the query throws and the
/// fallback to SQL is obvious. The dangerous case is the quiet one: the cluster is healthy, answers
/// every query, and simply never receives what was posted — because the worker is stopped, the
/// broker is unreachable, or a consumer is stuck. Then a comment is in the database and invisible
/// on the page, with no error anywhere. These tests write straight to SQL, which is the same
/// situation from the index's point of view, and assert the list still tells the truth.
/// </remarks>
[Collection(IntegrationTestSuite.Name)]
public sealed class SearchIndexFreshnessTests(CommentsApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _client = factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task A_comment_the_index_never_received_still_appears_in_the_list()
    {
        await InsertDirectlyIntoSqlAsync("Straight into SQL, never projected.");
        await factory.ResetCacheAsync();

        var list = await _client.GetFromJsonAsync<PagedResult<CommentListItemDto>>("/api/comments", Json);

        list!.TotalCount.ShouldBe(1);
        list.Items.Single().TextHtml.ShouldBe("Straight into SQL, never projected.");
    }

    /// <summary>
    /// Writes a comment the way a stalled pipeline leaves one: in SQL, with nothing published.
    /// </summary>
    /// <remarks>
    /// Raw SQL rather than the aggregate, because saving through the context would enqueue the
    /// outbox row that eventually indexes it — which is exactly what this test needs not to happen.
    /// The timestamp is far enough in the past to be past the grace period that ordinary lag gets.
    /// </remarks>
    private async Task InsertDirectlyIntoSqlAsync(string text)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var userId = Guid.CreateVersion7(createdAt);
        var commentId = Guid.CreateVersion7(createdAt);

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Users (Id, UserName, Email, HomePage, CreatedAt, LastPostedAt)
            VALUES ({userId}, 'Ghost', 'ghost@example.com', NULL, {createdAt}, {createdAt});

            INSERT INTO Comments
                (Id, RootId, ParentId, AuthorId, Path, Depth, TextHtml, TextPlain,
                 ClientIpHash, ClientUserAgent, ClientId, CreatedAt)
            VALUES
                ({commentId}, {commentId}, NULL, {userId}, {commentId.ToString("N")[..16]}, 1,
                 {text}, {text}, {new string('a', 64)}, NULL, NULL, {createdAt});
            """);
    }
}
