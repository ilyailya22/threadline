namespace Threadline.Comments.Application.Comments.Sanitization;

/// <summary>
/// Turns raw user input into text that is safe to render, or explains why it cannot.
/// </summary>
public interface ICommentTextSanitizer
{
    /// <summary>
    /// Validates and sanitises <paramref name="rawText"/>.
    /// </summary>
    /// <remarks>
    /// Contract, relied upon by <c>CommentBody.FromSanitized</c>:
    /// <list type="bullet">
    ///   <item>the returned HTML contains only the tags the assignment allows;</item>
    ///   <item>the returned HTML is well-formed XHTML — it round-trips through an XML parser;</item>
    ///   <item>every other <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>"</c> is entity-escaped, so no
    ///         markup the user typed can become markup the browser executes.</item>
    /// </list>
    /// </remarks>
    SanitizationResult Sanitize(string? rawText);
}
