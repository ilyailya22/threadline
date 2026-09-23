using Threadline.Comments.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Infrastructure.Search;

/// <inheritdoc cref="ISearchIndexFreshness"/>
/// <remarks>
/// <para>
/// Two single-row reads — the newest top-level comment in SQL, the newest document in the index —
/// and a comparison. Anything that stops the projection shows up here as SQL running ahead,
/// whether the worker is down, the broker is unreachable or a consumer is stuck.
/// </para>
/// <para>
/// The check runs only where it matters: inside the list query, which itself sits behind a
/// short-lived cache, so a busy site pays for it a few times a minute rather than per view. The
/// answer is remembered for the rest of the request, because one page never needs to ask twice.
/// </para>
/// </remarks>
public sealed partial class SearchIndexFreshness(
    ICommentReadRepository comments,
    ICommentSearchIndex searchIndex,
    IDateTimeProvider clock,
    ILogger<SearchIndexFreshness> logger) : ISearchIndexFreshness
{
    /// <summary>
    /// How far the index may trail SQL before the list stops trusting it. Ordinary lag is the
    /// batch window plus a refresh — tens of milliseconds — so seconds here means "stalled", not
    /// "busy", and a healthy system never takes the fallback.
    /// </summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(10);

    private bool? _current;

    public async ValueTask<bool> IsCurrentAsync(CancellationToken cancellationToken = default) =>
        _current ??= await MeasureAsync(clock.UtcNow, cancellationToken);

    private async Task<bool> MeasureAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var newestStored = await comments.GetNewestTopLevelCreatedAtAsync(cancellationToken);

            if (newestStored is null || now - newestStored.Value <= GracePeriod)
            {
                // Nothing to project, or too recent to judge: normal lag is not a stall.
                return true;
            }

            var newestIndexed = await searchIndex.GetNewestCreatedAtAsync(cancellationToken);

            if (newestIndexed >= newestStored)
            {
                return true;
            }

            LogIndexBehind(logger, newestStored.Value, newestIndexed);

            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The check must never be the thing that breaks the page. If it cannot be answered,
            // the list goes to Elasticsearch as usual, and the query's own fallback handles a
            // cluster that is actually down.
            LogCheckFailed(logger, exception);

            return true;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Search index is behind SQL: newest stored {NewestStored}, newest indexed {NewestIndexed}. Serving the list from SQL.")]
    private static partial void LogIndexBehind(
        ILogger logger,
        DateTimeOffset newestStored,
        DateTimeOffset? newestIndexed);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not measure search index freshness")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
