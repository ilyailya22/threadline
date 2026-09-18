namespace Threadline.Comments.Application.Common.Exceptions;

/// <summary>
/// One or more inputs were rejected. Carries per-field messages so the API can return an
/// RFC 9457 ProblemDetails the Angular form binds directly to its controls.
/// </summary>
public sealed class InputValidationException : Exception
{
    public InputValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") =>
        Errors = errors;

    public InputValidationException(string field, params string[] messages)
        : this(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = messages })
    {
    }

    public InputValidationException()
        : this(new Dictionary<string, string[]>(StringComparer.Ordinal))
    {
    }

    public InputValidationException(string message)
        : base(message) =>
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

    public InputValidationException(string message, Exception innerException)
        : base(message, innerException) =>
        Errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string[]> Errors { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal);
}
