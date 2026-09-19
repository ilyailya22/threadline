using Threadline.Comments.Application.Common.Models;
using Threadline.Comments.Domain.Comments;
using Shouldly;

namespace Threadline.Comments.UnitTests.Paging;

public sealed class ThreadCursorTests
{
    [Fact]
    public void A_cursor_round_trips_through_its_string_form()
    {
        var id = Guid.CreateVersion7();
        var path = CommentPath.ForReply(CommentPath.ForRoot(Guid.CreateVersion7()), id).Value;
        var cursor = new ThreadCursor(path, id);

        ThreadCursor.TryParse(cursor.ToString(), out var parsed).ShouldBeTrue();

        parsed.ShouldBe(cursor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-path")]
    [InlineData("0123456789abcdef")] // a bare path, without the id
    [InlineData("0123456789abcdef.not-a-guid")]
    [InlineData("0123456789ABCDEF.0192f3c1a4b07d21a1b2c3d4e5f60718")] // path must be lowercase hex
    [InlineData(".0192f3c1a4b07d21a1b2c3d4e5f60718")]
    public void Anything_malformed_is_refused(string? value) =>
        ThreadCursor.TryParse(value, out _).ShouldBeFalse();
}
