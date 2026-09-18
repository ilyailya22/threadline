using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// Request-scoped facts about the caller, supplied by the API layer. Keeping this behind an
/// interface is what lets the application layer record who posted a comment without referencing
/// <c>HttpContext</c>.
/// </summary>
public interface IClientContext
{
    /// <summary>Hex HMAC-SHA256 of the remote IP — see <see cref="ClientFingerprint"/>.</summary>
    string IpHash { get; }

    string? UserAgent { get; }

    Guid? ClientId { get; }
}
