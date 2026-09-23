using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.IntegrationTests.Infrastructure;
using Shouldly;
using SkiaSharp;

namespace Threadline.Comments.IntegrationTests;

/// <summary>
/// End-to-end tests against the real pipeline: HTTP in, SQL Server out, with every dependency
/// running in a container.
/// </summary>
[Collection(IntegrationTestSuite.Name)]
public sealed class CommentsApiTests(CommentsApiFactory factory) : IAsyncLifetime
{
    // Enums as names, the way the API writes them.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>A real PNG of the given size, so the upload path has something to downscale.</summary>
    private static byte[] WidePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.CornflowerBlue);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }

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

    // ---------------------------------------------------------------- posting

    [Fact]
    public async Task Posting_a_comment_stores_it_and_returns_201()
    {
        var response = await PostCommentAsync(text: "Hello <strong>world</strong>");

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreateCommentResultDto>(Json);

        created.ShouldNotBeNull();
        created.Id.ShouldNotBe(Guid.Empty);
        created.RootId.ShouldBe(created.Id);
        created.ParentId.ShouldBeNull();
        created.TextHtml.ShouldBe("Hello <strong>world</strong>");
    }

    [Fact]
    public async Task A_reply_belongs_to_its_parents_thread()
    {
        var root = await CreateCommentAsync("Root comment");
        var reply = await CreateCommentAsync("A reply", parentId: root.Id);
        var nested = await CreateCommentAsync("A reply to the reply", parentId: reply.Id);

        reply.RootId.ShouldBe(root.Id);
        nested.RootId.ShouldBe(root.Id);
        nested.ParentId.ShouldBe(reply.Id);

        var thread = await _client.GetFromJsonAsync<CommentThreadDto>(
            $"/api/comments/{root.Id}/thread", Json);

        thread.ShouldNotBeNull();
        thread.TotalCount.ShouldBe(3);

        // Depth-first: root, then its reply, then the reply to the reply — each node right after
        // its parent, which is what lets the client rebuild the nesting from a flat page.
        thread.Nodes.Select(n => n.Id).ShouldBe([root.Id, reply.Id, nested.Id]);
        thread.Nodes[2].ParentId.ShouldBe(reply.Id);
        thread.Nodes[2].Depth.ShouldBe(3);
        thread.HasMore.ShouldBeFalse();
    }

    [Fact]
    public async Task Replying_to_a_comment_that_does_not_exist_is_404()
    {
        var response = await PostCommentAsync(text: "Orphan", parentId: Guid.CreateVersion7());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The assignment's "каскадное отображение" with no depth limit in practice: this builds a
    /// twenty-level chain and reads it back in one request.
    /// </summary>
    [Fact]
    public async Task Deeply_nested_replies_are_stored_and_returned_in_order()
    {
        var root = await CreateCommentAsync("Level 1");
        var current = root;

        for (var level = 2; level <= 20; level++)
        {
            current = await CreateCommentAsync($"Level {level}", parentId: current.Id);
        }

        var thread = await _client.GetFromJsonAsync<CommentThreadDto>(
            $"/api/comments/{root.Id}/thread?maxDepth=25", Json);

        thread.ShouldNotBeNull();
        thread.TotalCount.ShouldBe(20);
        thread.Nodes.Select(n => n.Depth).ShouldBe(Enumerable.Range(1, 20));
        thread.Nodes[^1].TextHtml.ShouldContain("Level 20");
    }

    /// <summary>
    /// A thread is unbounded, so its endpoint pages. Walking every page with the cursor must return
    /// each comment exactly once, in depth-first order, with every node after its parent.
    /// </summary>
    [Fact]
    public async Task A_large_thread_is_paged_without_gaps_or_duplicates()
    {
        var root = await CreateCommentAsync("Big thread");
        var ids = new List<Guid> { root.Id };

        for (var i = 0; i < 12; i++)
        {
            var reply = await CreateCommentAsync($"Reply {i}", parentId: root.Id);
            ids.Add(reply.Id);

            if (i % 3 == 0)
            {
                ids.Add((await CreateCommentAsync($"Nested {i}", parentId: reply.Id)).Id);
            }
        }

        var seen = new List<CommentNodeDto>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var url = $"/api/comments/{root.Id}/thread?limit=5" + (cursor is null ? string.Empty : $"&after={cursor}");
            var page = await _client.GetFromJsonAsync<CommentThreadDto>(url, Json);

            page.ShouldNotBeNull();
            page.TotalCount.ShouldBe(ids.Count);
            page.Nodes.Count.ShouldBeLessThanOrEqualTo(5);

            seen.AddRange(page.Nodes);
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 20);

        seen.Select(n => n.Id).ShouldBe(ids, ignoreOrder: true);
        seen.Select(n => n.Id).Distinct().Count().ShouldBe(seen.Count);

        var position = seen.Select((n, index) => (n.Id, index)).ToDictionary(x => x.Id, x => x.index);
        seen.Where(n => n.ParentId is not null).ShouldAllBe(n => position[n.ParentId!.Value] < position[n.Id]);
    }

    [Fact]
    public async Task A_malformed_thread_cursor_is_a_400()
    {
        var root = await CreateCommentAsync("Thread");

        var response = await _client.GetAsync($"/api/comments/{root.Id}/thread?after=not-a-path");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- validation

    [Theory]
    [InlineData("userName", "не латиница")]
    [InlineData("userName", "")]
    [InlineData("email", "not-an-email")]
    [InlineData("homePage", "javascript:alert(1)")]
    public async Task Invalid_input_is_rejected_with_field_level_errors(string field, string value)
    {
        var form = ValidForm();
        form[field] = value;

        var response = await PostFormAsync(form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        problem.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(
            $"expected a field-level error for '{field}'");
    }

    [Fact]
    public async Task A_wrong_captcha_answer_is_rejected()
    {
        var form = ValidForm();
        form["captchaAnswer"] = "WRONG";

        var response = await PostFormAsync(form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("captchaAnswer");
    }

    [Fact]
    public async Task Unbalanced_markup_is_rejected_rather_than_silently_repaired()
    {
        var response = await PostCommentAsync(text: "<strong>never closed");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("text");
    }

    // ---------------------------------------------------------------- security

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    public async Task Xss_payloads_are_stored_inert(string payload)
    {
        var created = await CreateCommentAsync($"{payload} and some text");

        // Asserted on the parsed tree, not on substrings: the escaped text legitimately contains
        // "javascript:" as characters while being inert. What matters is which elements and
        // attributes exist once the markup is parsed.
        var tree = System.Xml.Linq.XElement.Parse($"<r>{created.TextHtml}</r>");

        tree.Descendants().Select(e => e.Name.LocalName).ShouldAllBe(n => n == "a" || n == "code" || n == "i" || n == "strong");
        tree.Descendants().SelectMany(e => e.Attributes()).ShouldAllBe(a => !a.Value.Contains("javascript:", StringComparison.OrdinalIgnoreCase));
        created.TextHtml.ShouldContain("&lt;");
    }

    /// <summary>
    /// The injection strings are stored as ordinary text, and the tables they name still exist
    /// afterwards. EF Core parameterises everything; nothing here is concatenated into SQL.
    /// </summary>
    [Theory]
    [InlineData("'; DROP TABLE Comments; --")]
    [InlineData("' OR '1'='1")]
    [InlineData("1' UNION SELECT NULL,NULL--")]
    public async Task Sql_injection_payloads_are_just_text(string payload)
    {
        var created = await CreateCommentAsync($"Look at this: {payload}");

        created.Id.ShouldNotBe(Guid.Empty);

        // If any of that had executed, this request would fail.
        var list = await WaitForListAsync("/api/comments", expectedTotal: 1);

        list.ShouldNotBeNull();
        list.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_column_name_cannot_be_injected_through_the_sort_parameter()
    {
        var response = await _client.GetAsync("/api/comments?sortBy=userName%3BDROP%20TABLE%20Users--");

        // The enum binder refuses it outright — the value never reaches a query.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------------------------------------------------------------- listing

    [Fact]
    public async Task The_list_is_lifo_by_default()
    {
        await CreateCommentAsync("First", userName: "Alpha");
        await Task.Delay(1_100); // distinct timestamps, and the ids are time-ordered anyway
        await CreateCommentAsync("Second", userName: "Bravo");

        var list = await WaitForListAsync("/api/comments", expectedTotal: 2);

        list.ShouldNotBeNull();
        list.Items.Count.ShouldBe(2);
        list.Items[0].TextHtml.ShouldContain("Second");
    }

    [Theory]
    [InlineData("userName", "ascending", "Alpha")]
    [InlineData("userName", "descending", "Charlie")]
    [InlineData("email", "ascending", "Alpha")]
    [InlineData("email", "descending", "Charlie")]
    public async Task The_list_sorts_by_every_field_in_both_directions(
        string sortBy,
        string direction,
        string expectedFirstUser)
    {
        await CreateCommentAsync("b", userName: "Bravo", email: "bravo@example.com");
        await CreateCommentAsync("c", userName: "Charlie", email: "charlie@example.com");
        await CreateCommentAsync("a", userName: "Alpha", email: "alpha@example.com");

        var list = await WaitForListAsync($"/api/comments?sortBy={sortBy}&direction={direction}", expectedTotal: 3);

        list.ShouldNotBeNull();
        list.Items[0].Author.UserName.ShouldBe(expectedFirstUser);
    }

    [Fact]
    public async Task Only_top_level_comments_appear_in_the_list()
    {
        var root = await CreateCommentAsync("Root");
        await CreateCommentAsync("Reply", parentId: root.Id);

        var list = await WaitForListAsync("/api/comments", expectedTotal: 1);

        list.ShouldNotBeNull();
        list.TotalCount.ShouldBe(1);
        list.Items.Single().Id.ShouldBe(root.Id);
    }

    [Fact]
    public async Task The_page_size_is_25()
    {
        for (var i = 0; i < 30; i++)
        {
            await CreateCommentAsync($"Comment {i}", userName: $"User{i}");
        }

        var first = await WaitForListAsync("/api/comments?page=1", expectedTotal: 30);
        var second = await _client.GetFromJsonAsync<PagedResult<CommentListItemDto>>(
            "/api/comments?page=2", Json);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();

        first.PageSize.ShouldBe(25);
        first.Items.Count.ShouldBe(25);
        first.TotalPages.ShouldBe(2);
        second.Items.Count.ShouldBe(5);

        // Nothing appears on both pages: the tie-break in the sort order is doing its job.
        first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- preview and rules

    [Fact]
    public async Task Preview_returns_exactly_what_would_be_stored()
    {
        const string text = "Try <i>this</i> & <script>that</script>";

        var previewResponse = await _client.PostAsJsonAsync("/api/comments/preview", new { text });
        previewResponse.EnsureSuccessStatusCode();

        var preview = await previewResponse.Content.ReadFromJsonAsync<CommentPreviewDto>(Json);
        var created = await CreateCommentAsync(text);

        preview.ShouldNotBeNull();
        preview.TextHtml.ShouldBe(created.TextHtml);
    }

    [Fact]
    public async Task The_published_validation_rules_match_what_the_server_enforces()
    {
        var rules = await _client.GetFromJsonAsync<JsonElement>("/api/validation-rules");

        var pattern = rules.GetProperty("userName").GetProperty("pattern").GetString();
        pattern.ShouldNotBeNullOrWhiteSpace();

        var regex = new System.Text.RegularExpressions.Regex(pattern!);

        // What the client is told to accept is what the server actually accepts.
        regex.IsMatch("Valid123").ShouldBeTrue();
        regex.IsMatch("не латиница").ShouldBeFalse();

        rules.GetProperty("pageSize").GetInt32().ShouldBe(Paging.DefaultPageSize);

        var tags = rules.GetProperty("allowedTags").EnumerateArray().Select(t => t.GetString()).ToArray();
        tags.ShouldBe(["a", "code", "i", "strong"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_captcha_is_a_png_with_an_id_header()
    {
        var response = await _client.GetAsync("/api/captcha");

        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/png");
        response.Headers.GetValues("X-Captcha-Id").Single().ShouldNotBeNullOrWhiteSpace();

        // A cached CAPTCHA is not a CAPTCHA.
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.ShouldBeGreaterThan(500);
        bytes[..4].ShouldBe([0x89, 0x50, 0x4E, 0x47]); // PNG signature
    }

    // ---------------------------------------------------------------- eventual consistency

    /// <summary>
    /// The reply count must be exact even when replies race their own thread through the pipeline.
    /// Replies are posted back to back so that some are saved before the root has been indexed —
    /// the case where an increment-based counter double-counts.
    /// </summary>
    [Fact]
    public async Task The_reply_count_is_exact_when_replies_race_the_indexer()
    {
        var root = await CreateCommentAsync("Popular thread");

        await Task.WhenAll(Enumerable.Range(1, 5).Select(i =>
            CreateCommentAsync($"Reply {i}", parentId: root.Id, userName: $"Replier{i}")));

        var deadline = DateTime.UtcNow.AddSeconds(20);
        CommentListItemDto? item = null;

        while (DateTime.UtcNow < deadline)
        {
            var list = await _client.GetFromJsonAsync<PagedResult<CommentListItemDto>>("/api/comments", Json);
            item = list?.Items.SingleOrDefault(i => i.Id == root.Id);

            if (item?.ReplyCount == 5)
            {
                break;
            }

            await Task.Delay(200);
        }

        item.ShouldNotBeNull();
        item.ReplyCount.ShouldBe(5);

        // And it stays at five: no late increment arrives to push it past the truth.
        await Task.Delay(1_500);

        await factory.ResetCacheAsync();
        var settled = await _client.GetFromJsonAsync<PagedResult<CommentListItemDto>>("/api/comments", Json);
        settled!.Items.Single(i => i.Id == root.Id).ReplyCount.ShouldBe(5);
    }

    // ---------------------------------------------------------------- attachments

    [Fact]
    public async Task An_uploaded_image_comes_back_already_downscaled_and_servable()
    {
        // The assignment says an oversized image is scaled down on upload, so the response — and
        // the blob behind it — must already be the small one. Nothing here waits for a worker.
        var response = await PostFormAsync(ValidForm(), ("wide.png", "image/png", WidePng(1600, 1200)));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<CreateCommentResultDto>(Json);
        var attachment = created!.Attachments.ShouldHaveSingleItem();

        attachment.Kind.ShouldBe(AttachmentKind.Image);
        attachment.OriginalFileName.ShouldBe("wide.png");
        attachment.ContentType.ShouldBe("image/png");
        attachment.Width.ShouldBe(320);
        attachment.Height.ShouldBe(240);
        attachment.ThumbnailUrl.ShouldNotBeNull();

        using var content = await _client.GetAsync(attachment.Url);
        content.StatusCode.ShouldBe(HttpStatusCode.OK);
        content.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");

        using var thumbnail = await _client.GetAsync(attachment.ThumbnailUrl);
        thumbnail.StatusCode.ShouldBe(HttpStatusCode.OK);
        thumbnail.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Reads the list until the search index has caught up, or gives up after 20 seconds.
    /// </summary>
    /// <remarks>
    /// The list is served from Elasticsearch, which the pipeline updates asynchronously after the
    /// write commits. Waiting for the expected state is the honest way to test an eventually
    /// consistent read model; asserting on the first read would test the timing, not the system.
    /// </remarks>
    private async Task<PagedResult<CommentListItemDto>> WaitForListAsync(string url, long expectedTotal)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        PagedResult<CommentListItemDto>? last = null;

        while (DateTime.UtcNow < deadline)
        {
            last = await _client.GetFromJsonAsync<PagedResult<CommentListItemDto>>(url, Json);

            if (last is not null && last.TotalCount >= expectedTotal)
            {
                return last;
            }

            await Task.Delay(200);
        }

        return last ?? throw new InvalidOperationException($"No response from {url}.");
    }

    private async Task<CreateCommentResultDto> CreateCommentAsync(
        string text,
        Guid? parentId = null,
        string userName = "Anonym",
        string email = "anonym@example.com")
    {
        var response = await PostCommentAsync(text, parentId, userName, email);

        response.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<CreateCommentResultDto>(Json))!;
    }

    private async Task<HttpResponseMessage> PostCommentAsync(
        string text,
        Guid? parentId = null,
        string userName = "Anonym",
        string email = "anonym@example.com")
    {
        var form = ValidForm();

        form["text"] = text;
        form["userName"] = userName;
        form["email"] = email;

        if (parentId is { } id)
        {
            form["parentId"] = id.ToString();
        }

        return await PostFormAsync(form);
    }

    private async Task<HttpResponseMessage> PostFormAsync(
        Dictionary<string, string> fields,
        (string Name, string ContentType, byte[] Bytes)? file = null)
    {
        // A real challenge is issued even though the bypass answers it: the Redis round trip is
        // part of what these tests are exercising.
        using var captcha = await _client.GetAsync("/api/captcha");
        captcha.EnsureSuccessStatusCode();

        fields["captchaId"] = captcha.Headers.GetValues("X-Captcha-Id").Single();

        using var content = new MultipartFormDataContent();

        foreach (var (key, value) in fields)
        {
            content.Add(new StringContent(value), key);
        }

        if (file is { } upload)
        {
            var bytes = new ByteArrayContent(upload.Bytes);
            bytes.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
            content.Add(bytes, "file", upload.Name);
        }

        return await _client.PostAsync("/api/comments", content);
    }

    private static Dictionary<string, string> ValidForm() =>
        new(StringComparer.Ordinal)
        {
            ["userName"] = "Anonym",
            ["email"] = "anonym@example.com",
            ["text"] = "A perfectly ordinary comment.",
            ["captchaAnswer"] = CommentsApiFactory.CaptchaAnswer,
        };
}
