namespace Threadline.Comments.Application.Comments.Sanitization;

/// <summary>A single reason the submitted text was rejected.</summary>
/// <param name="Code">Stable machine-readable code — the Angular client maps it to a localised message.</param>
/// <param name="Message">Human-readable English description.</param>
/// <param name="Position">Zero-based offset in the original text, when known.</param>
public sealed record SanitizationError(string Code, string Message, int? Position = null);

/// <summary>Successfully sanitised text, in both of the forms the system stores.</summary>
public sealed record SanitizedText(string Html, string PlainText);

/// <summary>
/// Outcome of sanitising a comment: either a safe, well-formed value or the list of reasons it was
/// refused. Modelled as a result object rather than exceptions because rejection is an ordinary,
/// expected outcome of user input and each error maps to a field-level message in the UI.
/// </summary>
public sealed class SanitizationResult
{
    private SanitizationResult(SanitizedText? value, IReadOnlyList<SanitizationError> errors)
    {
        Value = value;
        Errors = errors;
    }

    public SanitizedText? Value { get; }

    public IReadOnlyList<SanitizationError> Errors { get; }

    public bool IsValid => Value is not null;

    public static SanitizationResult Success(string html, string plainText) =>
        new(new SanitizedText(html, plainText), []);

    public static SanitizationResult Failure(params SanitizationError[] errors) =>
        new(null, errors);

    public static SanitizationResult Failure(IReadOnlyList<SanitizationError> errors) =>
        new(null, errors);
}
