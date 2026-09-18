using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Comments;

/// <summary>
/// The message text, in both the form we render (<see cref="Html"/>, already sanitised to the
/// allowed tag set) and the form we index and search (<see cref="PlainText"/>).
/// </summary>
/// <remarks>
/// The domain deliberately does <em>not</em> sanitise — that belongs to a dedicated, unit-tested
/// service in the application layer. What the domain guarantees is that a body can only be
/// constructed from a value that has been through that service, which is why the factory is
/// <see cref="FromSanitized"/> and there is no public constructor taking raw input.
/// </remarks>
public sealed class CommentBody : ValueObject
{
    public const int MaxHtmlLength = 20_000;
    public const int MaxPlainTextLength = 20_000;

    /// <summary>Length of the excerpt shown in the top-level table.</summary>
    public const int PreviewLength = 200;

    private CommentBody(string html, string plainText)
    {
        Html = html;
        PlainText = plainText;
    }

    /// <summary>Sanitised XHTML, safe to render.</summary>
    public string Html { get; }

    /// <summary>Tag-free text used for search indexing and previews.</summary>
    public string PlainText { get; }

    public static CommentBody FromSanitized(string html, string plainText)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(plainText))
        {
            throw new DomainException("Comment text is required.");
        }

        if (html.Length > MaxHtmlLength)
        {
            throw new DomainException($"Comment text must not exceed {MaxHtmlLength} characters.");
        }

        return new CommentBody(html, plainText[..Math.Min(plainText.Length, MaxPlainTextLength)]);
    }

    /// <summary>
    /// The excerpt of a plain-text body shown in the table. Static because read models hold the plain
    /// text as a string, not as a <see cref="CommentBody"/>; both of them must cut it the same way.
    /// </summary>
    public static string ToPreview(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        return plainText.Length <= PreviewLength ? plainText : string.Concat(plainText.AsSpan(0, PreviewLength), "…");
    }

    public override string ToString() => PlainText;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Html;
    }
}
