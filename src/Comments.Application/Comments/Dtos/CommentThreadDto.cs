namespace Threadline.Comments.Application.Comments.Dtos;

/// <summary>
/// One page of a thread, as a flat list in depth-first display order.
/// </summary>
/// <remarks>
/// Flat rather than nested, and paged, because a thread is unbounded: a popular one in the seeded
/// dataset has almost fourteen thousand replies, which as a single nested response was 6.5 MB. Pages
/// are cut on the materialised path, which sorts depth-first, so every page is a contiguous run of
/// the tree in which each node's ancestors have already appeared — the client can append pages and
/// rebuild the nesting from <see cref="CommentNodeDto.ParentId"/> without ever seeing an orphan.
/// </remarks>
/// <param name="RootId">The thread's top-level comment.</param>
/// <param name="TotalCount">Comments in the thread within the requested depth.</param>
/// <param name="Nodes">This page, depth-first.</param>
/// <param name="NextCursor">Pass as <c>after</c> to fetch the next page; <see langword="null"/> at the end.</param>
public sealed record CommentThreadDto(
    Guid RootId,
    int TotalCount,
    IReadOnlyList<CommentNodeDto> Nodes,
    string? NextCursor)
{
    public bool HasMore => NextCursor is not null;
}
