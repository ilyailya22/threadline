using System.Diagnostics.CodeAnalysis;

namespace Threadline.Comments.Application.Comments.Sanitization;

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

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsValid => Value is not null;

    public static SanitizationResult Success(string html, string plainText) =>
        new(new SanitizedText(html, plainText), []);

    public static SanitizationResult Failure(params SanitizationError[] errors) =>
        new(null, errors);

    public static SanitizationResult Failure(IReadOnlyList<SanitizationError> errors) =>
        new(null, errors);
}
