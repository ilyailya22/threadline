using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Common;
using Shouldly;

namespace Threadline.Comments.UnitTests.Domain;

/// <summary>
/// The materialised path is what makes unlimited nesting cheap, so its properties are worth
/// asserting rather than assuming: depth-first ordering, correct depth, and a hard cap.
/// </summary>
public sealed class CommentPathTests
{
    [Fact]
    public void Root_path_is_one_segment_deep()
    {
        var path = CommentPath.ForRoot(Guid.CreateVersion7());

        path.Depth.ShouldBe(1);
        path.IsRoot.ShouldBeTrue();
        path.Value.Length.ShouldBe(CommentPath.SegmentLength);
        path.Parent.ShouldBeNull();
    }

    [Fact]
    public void Reply_path_extends_the_parent_and_increments_depth()
    {
        var root = CommentPath.ForRoot(Guid.CreateVersion7());
        var reply = CommentPath.ForReply(root, Guid.CreateVersion7());

        reply.Depth.ShouldBe(2);
        reply.Value.ShouldStartWith(root.Value);
        reply.IsDescendantOf(root).ShouldBeTrue();
        reply.Parent.ShouldBe(root);
    }

    [Fact]
    public void A_path_is_not_a_descendant_of_itself()
    {
        var path = CommentPath.ForRoot(Guid.CreateVersion7());

        path.IsDescendantOf(path).ShouldBeFalse();
    }

    /// <summary>
    /// The property the whole design rests on: sorting paths as strings produces exactly the order
    /// a cascading view needs, so the database can do the ordering with one index.
    /// </summary>
    [Fact]
    public void Ordinal_sorting_of_paths_is_depth_first_tree_order()
    {
        var rootId = Guid.CreateVersion7(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var root = CommentPath.ForRoot(rootId);

        var firstReplyId = Guid.CreateVersion7(new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero));
        var firstReply = CommentPath.ForReply(root, firstReplyId);

        var nestedId = Guid.CreateVersion7(new DateTimeOffset(2026, 1, 1, 0, 2, 0, TimeSpan.Zero));
        var nested = CommentPath.ForReply(firstReply, nestedId);

        var secondReplyId = Guid.CreateVersion7(new DateTimeOffset(2026, 1, 1, 0, 3, 0, TimeSpan.Zero));
        var secondReply = CommentPath.ForReply(root, secondReplyId);

        var sorted = new[] { secondReply, nested, root, firstReply }
            .OrderBy(p => p.Value, StringComparer.Ordinal)
            .ToArray();

        // root → its first reply → that reply's child → root's second reply.
        sorted.ShouldBe([root, firstReply, nested, secondReply]);
    }

    [Fact]
    public void Siblings_sort_by_creation_time_because_ids_are_uuid_v7()
    {
        var root = CommentPath.ForRoot(Guid.CreateVersion7());

        var earlier = CommentPath.ForReply(
            root,
            Guid.CreateVersion7(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero)));

        var later = CommentPath.ForReply(
            root,
            Guid.CreateVersion7(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)));

        (earlier < later).ShouldBeTrue();
    }

    [Fact]
    public void Nesting_beyond_the_cap_is_refused()
    {
        var path = CommentPath.ForRoot(Guid.CreateVersion7());

        for (var depth = 1; depth < CommentPath.MaxDepth; depth++)
        {
            path = CommentPath.ForReply(path, Guid.CreateVersion7());
        }

        path.Depth.ShouldBe(CommentPath.MaxDepth);

        Should.Throw<DomainException>(() => CommentPath.ForReply(path, Guid.CreateVersion7()));
    }

    [Fact]
    public void Path_at_the_cap_still_fits_a_sql_server_index_key()
    {
        // 1700 bytes is the non-clustered index key limit; the column is non-Unicode, so one
        // character is one byte. If this ever fails, IX_Comments_RootId_Path silently stops being
        // creatable — better to learn that here than from a failed migration.
        CommentPath.MaxLength.ShouldBeLessThan(1700 - 16);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tooshort")]
    [InlineData("0123456789abcdef0")]
    public void Malformed_stored_values_are_rejected(string value)
    {
        Should.Throw<DomainException>(() => CommentPath.FromStorage(value));
    }

    [Fact]
    public void A_stored_path_round_trips()
    {
        var original = CommentPath.ForReply(CommentPath.ForRoot(Guid.CreateVersion7()), Guid.CreateVersion7());

        CommentPath.FromStorage(original.Value).ShouldBe(original);
    }
}
