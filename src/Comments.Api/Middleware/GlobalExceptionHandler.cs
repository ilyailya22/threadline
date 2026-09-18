using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Threadline.Comments.Api.Middleware;

/// <summary>
/// Turns exceptions into RFC 9457 ProblemDetails.
/// </summary>
/// <remarks>
/// <para>
/// One place decides what the client sees, which is what stops a stack trace or a SQL error message
/// from reaching a browser. Anything unrecognised becomes a bare 500 with a correlation id: the
/// operator can find the details in the logs by that id, the attacker learns nothing.
/// </para>
/// <para>
/// Validation failures keep their per-field structure, because the Angular form binds those
/// messages straight onto its controls.
/// </para>
/// </remarks>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var traceId = httpContext.TraceIdentifier;

        var problem = exception switch
        {
            InputValidationException validation => Validation(validation),
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = notFound.Message,
            },
            DomainException domain => new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "The request could not be processed",
                Detail = domain.Message,
            },
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad request",
                Detail = badRequest.Message,
            },
            OperationCanceledException => new ProblemDetails
            {
                Status = StatusCodes.Status499ClientClosedRequest,
                Title = "Request cancelled",
            },
            _ => null,
        };

        if (problem is null)
        {
            LogUnhandled(logger, traceId, exception);

            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred",

                // Deliberately generic. The correlation id is the bridge to the logs.
                Detail = $"Something went wrong. Reference: {traceId}",
            };
        }
        else
        {
            LogHandled(logger, problem.Status ?? 0, exception);
        }

        problem.Extensions["traceId"] = traceId;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static ValidationProblemDetails Validation(InputValidationException exception)
    {
        var problem = new ValidationProblemDetails(
            exception.Errors.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred",
        };

        return problem;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception (traceId {TraceId})")]
    private static partial void LogUnhandled(ILogger logger, string traceId, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Returning {StatusCode} for a handled exception")]
    private static partial void LogHandled(ILogger logger, int statusCode, Exception exception);
}
