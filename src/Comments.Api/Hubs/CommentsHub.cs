using Microsoft.AspNetCore.SignalR;

namespace Threadline.Comments.Api.Hubs;

/// <summary>
/// Live updates for the comment board.
/// </summary>
/// <remarks>
/// <para>
/// Clients join a group per thread rather than receiving everything. On a board with a million
/// comments, broadcasting every new reply to every connected browser would be the first thing to
/// fall over; a client watching one thread should receive that thread's traffic and nothing else.
/// </para>
/// <para>
/// The hub carries no authorisation because the board is public and read-only over the socket:
/// nothing can be written through it. Posting goes through the HTTP API, where the CAPTCHA and the
/// rate limiter live.
/// </para>
/// </remarks>
public sealed class CommentsHub : Hub
{
    /// <summary>Group that receives every new top-level comment.</summary>
    public const string TopLevelGroup = "comments:top-level";

    public const string CommentCreated = "commentCreated";

    public const string AttachmentReady = "attachmentReady";

    public override Task OnConnectedAsync() =>
        Groups.AddToGroupAsync(Context.ConnectionId, TopLevelGroup);

    /// <summary>Subscribes this connection to one thread's replies.</summary>
    public Task WatchThread(Guid rootId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, ThreadGroup(rootId));

    public Task UnwatchThread(Guid rootId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, ThreadGroup(rootId));

    public static string ThreadGroup(Guid rootId) => $"comments:thread:{rootId:N}";
}
