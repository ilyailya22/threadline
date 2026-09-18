using Threadline.Comments.Application.Common.Models;
using Shouldly;
using Limits = Threadline.Comments.Application.Common.Models.Paging;

namespace Threadline.Comments.UnitTests.Paging;

public sealed class PagedResultTests
{
    [Fact]
    public void Total_count_is_reported_exactly_even_past_the_paging_ceiling()
    {
        var result = new PagedResult<int>([], 1, 25, 48_447);

        result.TotalCount.ShouldBe(48_447);
    }

    [Fact]
    public void Total_pages_stop_at_the_deep_paging_ceiling()
    {
        var result = new PagedResult<int>([], 1, 25, 48_447);

        result.TotalPages.ShouldBe(Limits.MaxOffset / 25);
    }

    [Fact]
    public void Small_results_are_paged_normally()
    {
        var result = new PagedResult<int>([], 1, 25, 30);

        result.TotalPages.ShouldBe(2);
        result.HasNext.ShouldBeTrue();
        result.HasPrevious.ShouldBeFalse();
    }
}
