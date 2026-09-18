using Threadline.Comments.Domain.Comments;
using Shouldly;

namespace Threadline.Comments.UnitTests.Domain;

public sealed class CommentBodyTests
{
    [Fact]
    public void A_short_text_is_its_own_preview() =>
        CommentBody.ToPreview("Hello").ShouldBe("Hello");

    [Fact]
    public void A_long_text_is_cut_at_the_preview_length_and_marked_as_cut()
    {
        var preview = CommentBody.ToPreview(new string('a', CommentBody.PreviewLength + 50));

        preview.Length.ShouldBe(CommentBody.PreviewLength + 1);
        preview.ShouldEndWith("…");
    }

    [Fact]
    public void A_text_of_exactly_the_preview_length_is_not_marked_as_cut() =>
        CommentBody.ToPreview(new string('a', CommentBody.PreviewLength)).ShouldNotEndWith("…");
}
