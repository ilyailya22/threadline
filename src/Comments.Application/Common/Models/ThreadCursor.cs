using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Application.Common.Models;

/// <summary>
/// Position in a thread for keyset paging: the last node of the previous page.
/// </summary>
/// <remarks>
/// The path alone is not a unique key. A path segment is the time-ordered prefix of a UUID v7 —
/// 48 bits of milliseconds plus 12 random bits — so two replies to the same parent within the same
/// millisecond share a segment one time in 4096. Ordering by (path, id) makes the order total, and
/// a cursor carrying both can never skip a node that ties with the last one seen. The clustered key
/// is part of every SQL Server index, so the tie-breaker costs no extra sort.
/// </remarks>
public sealed record ThreadCursor(string Path, Guid Id)
{
    private const char Separator = '.';

    /// <summary>Opaque to clients: they pass back exactly what they were given.</summary>
    public override string ToString() => $"{Path}{Separator}{Id:N}";

    /// <summary>Parses a cursor received from a client; anything malformed is <see langword="false"/>.</summary>
    public static bool TryParse(string? value, out ThreadCursor? cursor)
    {
        cursor = null;

        var separator = value?.LastIndexOf(Separator) ?? -1;

        if (value is null || separator <= 0 || !Guid.TryParseExact(value[(separator + 1)..], "N", out var id))
        {
            return false;
        }

        try
        {
            // Through the domain type, so a cursor is held to the same rules as a stored path.
            cursor = new ThreadCursor(CommentPath.FromStorage(value[..separator]).Value, id);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}
