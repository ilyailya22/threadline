using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Comments;

/// <summary>
/// Materialised path of a comment inside its thread — the single design decision that makes
/// unlimited cascading replies cheap at a million rows.
/// </summary>
/// <remarks>
/// <para>
/// A path is a concatenation of fixed-width <see cref="SegmentLength"/>-character segments, one per
/// ancestor plus one for the comment itself, e.g.
/// <c>0192f3c1a4b07d21` + `0192f3c1c8e14a05</c>. Because the segments are fixed width, no separator
/// is needed and lexicographic ordering of the whole string equals depth-first ordering of the tree.
/// </para>
/// <para>
/// Each segment is the first 16 hex characters of the comment's UUID v7, which encode the 48-bit
/// creation timestamp. Two consequences: siblings sort chronologically for free, and building a
/// path needs no counter, no <c>MAX(path) + 1</c> query and therefore no write contention — which
/// is exactly what breaks under the 100k users / 24h target the assignment sets.
/// </para>
/// <para>
/// Fetching a whole thread is then one index range scan:
/// <c>WHERE RootId = @root ORDER BY Path</c> — no recursive CTE, no N+1, and the rows come back
/// already in display order.
/// </para>
/// </remarks>
public sealed class CommentPath : ValueObject, IComparable<CommentPath>
{
    public const int SegmentLength = 16;

    /// <summary>
    /// Maximum nesting depth. The assignment asks for unlimited replies <em>per comment</em>
    /// (breadth), which this design gives; depth is capped so that the column stays inside the
    /// 1700-byte SQL Server index key limit and a malicious client cannot build an unbounded chain.
    /// </summary>
    public const int MaxDepth = 64;

    public const int MaxLength = SegmentLength * MaxDepth;

    private CommentPath(string value) => Value = value;

    public string Value { get; }

    /// <summary>1 for a top-level comment, 2 for a direct reply, and so on.</summary>
    public int Depth => Value.Length / SegmentLength;

    public bool IsRoot => Depth == 1;

    /// <summary>The path prefix identifying every ancestor, or <see langword="null"/> at the root.</summary>
    public CommentPath? Parent =>
        IsRoot ? null : new CommentPath(Value[..^SegmentLength]);

    public static CommentPath ForRoot(Guid commentId) => new(Segment(commentId));

    public static CommentPath ForReply(CommentPath parentPath, Guid commentId)
    {
        ArgumentNullException.ThrowIfNull(parentPath);

        if (parentPath.Depth >= MaxDepth)
        {
            throw new DomainException(
                $"Replies cannot be nested deeper than {MaxDepth} levels.");
        }

        return new CommentPath(parentPath.Value + Segment(commentId));
    }

    /// <summary>Rehydrates a path read back from the database.</summary>
    public static CommentPath FromStorage(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length % SegmentLength != 0
            || value.Length > MaxLength)
        {
            throw new DomainException($"'{value}' is not a valid comment path.");
        }

        return new CommentPath(value);
    }

    /// <summary>
    /// Prefix used by <c>LIKE @prefix + '%'</c> subtree queries. Kept as a method rather than a raw
    /// string so callers cannot accidentally build the pattern themselves and forget to escape it.
    /// </summary>
    public string ToSubtreePrefix() => Value;

    public bool IsDescendantOf(CommentPath other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Value.Length > other.Value.Length
            && Value.StartsWith(other.Value, StringComparison.Ordinal);
    }

    public int CompareTo(CommentPath? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    public static bool operator ==(CommentPath? left, CommentPath? right) => Equals(left, right);

    public static bool operator !=(CommentPath? left, CommentPath? right) => !Equals(left, right);

    public static bool operator <(CommentPath? left, CommentPath? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(CommentPath? left, CommentPath? right) =>
        left is null || left.CompareTo(right) <= 0;

    public static bool operator >(CommentPath? left, CommentPath? right) =>
        left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(CommentPath? left, CommentPath? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    public override bool Equals(object? obj) => base.Equals(obj);

    public override int GetHashCode() => base.GetHashCode();

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    private static string Segment(Guid commentId)
    {
        if (commentId == Guid.Empty)
        {
            throw new DomainException("Cannot build a comment path from an empty id.");
        }

        // "N" format = 32 lowercase hex chars, no dashes. The first 16 of them are the UUID v7
        // timestamp (48 bits) + version + rand_a, so the prefix is monotonic in creation time.
        return commentId.ToString("N")[..SegmentLength];
    }
}
