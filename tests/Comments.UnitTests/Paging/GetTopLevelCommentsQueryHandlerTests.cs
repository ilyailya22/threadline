using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Comments.Queries.GetTopLevelComments;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Application.Common.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shouldly;

namespace Threadline.Comments.UnitTests.Paging;

public sealed class GetTopLevelCommentsQueryHandlerTests
{
    private readonly Mock<ICommentSearchIndex> _search = new();
    private readonly Mock<ICommentReadRepository> _sql = new();

    [Fact]
    public async Task The_last_page_inside_the_offset_ceiling_is_served()
    {
        var lastPage = Threadline.Comments.Application.Common.Models.Paging.MaxOffset / Page.Size;
        SearchReturnsEmptyPage();

        var result = await CreateHandler().Handle(Query(lastPage), CancellationToken.None);

        result.Page.ShouldBe(lastPage);
    }

    [Fact]
    public async Task A_page_whose_window_ends_past_the_ceiling_is_rejected()
    {
        var firstTooFar = (Threadline.Comments.Application.Common.Models.Paging.MaxOffset / Page.Size) + 1;

        await Should.ThrowAsync<InputValidationException>(
            () => CreateHandler().Handle(Query(firstTooFar), CancellationToken.None));

        _search.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task When_search_is_down_the_list_is_served_from_sql()
    {
        _search
            .Setup(s => s.QueryTopLevelAsync(It.IsAny<CommentPageRequest>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));
        _sql
            .Setup(s => s.GetTopLevelAsync(It.IsAny<CommentPageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<CommentListItemDto>([], 1, Page.Size, 7));

        var result = await CreateHandler().Handle(Query(1), CancellationToken.None);

        result.TotalCount.ShouldBe(7);
    }

    private void SearchReturnsEmptyPage() =>
        _search
            .Setup(s => s.QueryTopLevelAsync(It.IsAny<CommentPageRequest>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CommentPageRequest request, string? _, CancellationToken _) =>
                new PagedResult<CommentListItemDto>([], request.Page, request.PageSize, 48_447));

    private static GetTopLevelCommentsQuery Query(int page) => new(new CommentPageRequest(page, Page.Size));

    private GetTopLevelCommentsQueryHandler CreateHandler() =>
        new(_search.Object, _sql.Object, new PassThroughCache(), NullLogger<GetTopLevelCommentsQueryHandler>.Instance);

    private static class Page
    {
        public const int Size = 25;
    }

    /// <summary>A cache that never holds anything, so every call reaches the handler's read path.</summary>
    private sealed class PassThroughCache : ICommentCache
    {
        public async Task<PagedResult<CommentListItemDto>> GetOrCreateTopLevelAsync(
            string key,
            Func<CancellationToken, ValueTask<PagedResult<CommentListItemDto>>> factory,
            CancellationToken cancellationToken = default) =>
            await factory(cancellationToken);

        public Task InvalidateTopLevelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
