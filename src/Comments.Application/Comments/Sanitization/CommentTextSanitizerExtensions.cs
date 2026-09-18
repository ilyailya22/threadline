using Threadline.Comments.Application.Common.Exceptions;

namespace Threadline.Comments.Application.Comments.Sanitization;

public static class CommentTextSanitizerExtensions
{
    /// <summary>Name of the form field the sanitiser's errors belong to.</summary>
    public const string TextField = "text";

    /// <summary>
    /// Sanitises the text or rejects it with field-level errors. Shared by posting and previewing, so
    /// a preview can never accept something that posting would refuse.
    /// </summary>
    public static SanitizedText SanitizeOrThrow(this ICommentTextSanitizer sanitizer, string? rawText)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);

        var result = sanitizer.Sanitize(rawText);

        return result.IsValid
            ? result.Value
            : throw new InputValidationException(TextField, [.. result.Errors.Select(e => e.Message)]);
    }
}
