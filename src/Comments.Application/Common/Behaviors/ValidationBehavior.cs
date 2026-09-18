using Threadline.Comments.Application.Common.Exceptions;
using FluentValidation;
using MediatR;

namespace Threadline.Comments.Application.Common.Behaviors;

/// <summary>
/// Runs every registered <see cref="IValidator{T}"/> for the request before the handler sees it.
/// </summary>
/// <remarks>
/// Server-side validation is a requirement of the assignment, and putting it in the pipeline rather
/// than in each handler means it cannot be forgotten when a new command is added: registering a
/// validator is enough, and a command with no validator is a visible, greppable fact rather than a
/// silent hole.
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var applicable = validators as IValidator<TRequest>[] ?? [.. validators];

        if (applicable.Length == 0)
        {
            return await next(cancellationToken);
        }

        var context = new ValidationContext<TRequest>(request);

        var results = await Task.WhenAll(
            applicable.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToArray();

        if (failures.Length > 0)
        {
            var errors = failures
                .GroupBy(f => ToCamelCase(f.PropertyName), StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);

            throw new InputValidationException(errors);
        }

        return await next(cancellationToken);
    }

    /// <summary>Field names are emitted in the casing the Angular form uses for its controls.</summary>
    private static string ToCamelCase(string propertyName) =>
        string.IsNullOrEmpty(propertyName) || char.IsLower(propertyName[0])
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
