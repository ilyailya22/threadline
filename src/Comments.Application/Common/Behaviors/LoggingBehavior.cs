using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Threadline.Comments.Application.Common.Behaviors;

/// <summary>
/// Structured, low-noise logging around every request, plus a warning when a handler crosses the
/// latency budget. Under load the slow-request warnings are the first thing that tells you which
/// handler regressed, without turning on verbose logging for everything.
/// </summary>
public sealed partial class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Anything slower than this is worth a look; see docs/LOAD-TESTING.md for the SLOs.</summary>
    private const long SlowRequestMs = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var name = typeof(TRequest).Name;
        var timestamp = Stopwatch.GetTimestamp();

        try
        {
            var response = await next(cancellationToken);

            var elapsed = (long)Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            if (elapsed >= SlowRequestMs)
            {
                LogSlow(logger, name, elapsed);
            }
            else
            {
                LogHandled(logger, name, elapsed);
            }

            return response;
        }
        catch (Exception exception)
        {
            LogFailed(logger, name, exception);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{RequestName} handled in {ElapsedMs} ms")]
    private static partial void LogHandled(ILogger logger, string requestName, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} was slow: {ElapsedMs} ms")]
    private static partial void LogSlow(ILogger logger, string requestName, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "{RequestName} failed")]
    private static partial void LogFailed(ILogger logger, string requestName, Exception exception);
}
