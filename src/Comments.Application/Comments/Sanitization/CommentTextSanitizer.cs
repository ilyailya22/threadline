using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Threadline.Comments.Application.Comments.Sanitization;

/// <summary>
/// The XSS boundary of the whole system, and the implementation of the assignment's
/// "Регулярные выражения" requirements.
/// </summary>
/// <remarks>
/// <para><b>How it works.</b> The input is tokenised into tags and text runs. A tag is kept only if
/// it is one of <c>&lt;a href title&gt;</c>, <c>&lt;code&gt;</c>, <c>&lt;i&gt;</c>,
/// <c>&lt;strong&gt;</c> and is spelled exactly as XHTML requires; anything else — including
/// <c>&lt;script&gt;</c>, <c>&lt;img onerror&gt;</c>, a stray <c>&lt;</c> or a mismatched quote — is
/// entity-escaped and ends up on screen as literal text. Open tags are pushed on a stack and
/// closing tags must match the top of it, which is the "проверка на закрытие тегов" the assignment
/// asks for: unbalanced input is rejected with a field error instead of being silently repaired.</para>
///
/// <para><b>Why an allowlist and not a blocklist.</b> Blocklists lose: there are too many ways to
/// spell an attack (<c>&lt;scr&lt;script&gt;ipt&gt;</c>, <c>&lt;a href="jav&amp;#x09;ascript:"&gt;</c>,
/// SVG event handlers, CSS expressions). Here nothing survives unless it is on a four-item list,
/// so a payload we have never seen still comes out as text.</para>
///
/// <para><b>Why the output is re-parsed as XML.</b> The final <see cref="XmlReader"/> pass is not
/// how correctness is achieved — it is how it is <em>verified</em>. If a future change to this class
/// ever produced markup that is not well-formed, the parse fails and the comment is refused rather
/// than stored. DTD processing and entity resolution are disabled, so the check itself cannot be
/// turned into an XXE or billion-laughs vector.</para>
/// </remarks>
public sealed partial class CommentTextSanitizer : ICommentTextSanitizer
{
    /// <summary>The exact tag set the assignment permits.</summary>
    public static readonly IReadOnlySet<string> AllowedTags =
        new HashSet<string>(StringComparer.Ordinal) { "a", "code", "i", "strong" };

    /// <summary>Attributes permitted on <c>&lt;a&gt;</c>. No other tag may carry any attribute.</summary>
    public static readonly IReadOnlySet<string> AllowedAnchorAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "href", "title" };

    private static readonly string[] AllowedLinkSchemes =
        [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    private const int MaxRawLength = 20_000;
    private const int MaxNestingDepth = 16;

    public SanitizationResult Sanitize(string? rawText)
    {
        var input = (rawText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        if (input.Length == 0)
        {
            return SanitizationResult.Failure(
                new SanitizationError("text.required", "Message text is required."));
        }

        if (input.Length > MaxRawLength)
        {
            return SanitizationResult.Failure(new SanitizationError(
                "text.too_long",
                $"Message text must not exceed {MaxRawLength} characters."));
        }

        var errors = new List<SanitizationError>();
        var html = new StringBuilder(input.Length + 32);
        var plain = new StringBuilder(input.Length);
        var openTags = new Stack<OpenTag>();

        var cursor = 0;
        foreach (var token in TagToken().EnumerateMatches(input))
        {
            AppendText(input.AsSpan(cursor, token.Index - cursor), html, plain);
            cursor = token.Index + token.Length;

            var tag = input.Substring(token.Index, token.Length);
            HandleTag(tag, token.Index, html, openTags, errors);

            if (errors.Count > 0)
            {
                // Stop at the first structural problem: reporting "unexpected </strong>" fifteen
                // times for one missing tag helps nobody.
                break;
            }
        }

        if (errors.Count == 0)
        {
            AppendText(input.AsSpan(cursor), html, plain);

            // Demoted tags are text, so leaving one "unclosed" is fine; only a real tag must balance.
            var unclosed = openTags.FirstOrDefault(t => !t.Demoted);

            if (unclosed is not null)
            {
                errors.Add(new SanitizationError(
                    "text.unclosed_tag",
                    $"Tag <{unclosed.Name}> is never closed."));
            }
        }

        if (errors.Count > 0)
        {
            return SanitizationResult.Failure(errors);
        }

        var result = html.ToString();

        if (!IsWellFormedXhtml(result, out var xmlError))
        {
            return SanitizationResult.Failure(new SanitizationError(
                "text.invalid_xhtml",
                $"Message text is not valid XHTML: {xmlError}"));
        }

        var plainText = plain.ToString().Trim();

        return plainText.Length == 0
            ? SanitizationResult.Failure(
                new SanitizationError("text.required", "Message text is required."))
            : SanitizationResult.Success(result, plainText);
    }

    private static void HandleTag(
        string tag,
        int position,
        StringBuilder html,
        Stack<OpenTag> openTags,
        List<SanitizationError> errors)
    {
        var close = ClosingTag().Match(tag);
        if (close.Success)
        {
            var name = close.Groups["name"].Value.ToLowerInvariant();

            if (!AllowedTags.Contains(name))
            {
                AppendEscaped(tag, html);
                return;
            }

            // Demoted openings that were never closed are just text; they must not stand between a
            // real closing tag and the real opening it belongs to.
            while (openTags.Count > 0 && openTags.Peek().Demoted && openTags.Peek().Name != name)
            {
                openTags.Pop();
            }

            if (openTags.Count == 0 || openTags.Peek().Name != name)
            {
                errors.Add(new SanitizationError(
                    "text.unexpected_closing_tag",
                    openTags.Count == 0
                        ? $"Closing tag </{name}> has no matching opening tag."
                        : $"Closing tag </{name}> does not match the still-open <{openTags.Peek().Name}>.",
                    position));
                return;
            }

            var opening = openTags.Pop();

            // The partner of a demoted opening is demoted too. Without this,
            // <a href="javascript:…">x</a> would escape the opening tag and then reject the whole
            // comment because the closing one looked orphaned — punishing the user for markup we
            // had already made harmless.
            if (opening.Demoted)
            {
                AppendEscaped(tag, html);
            }
            else
            {
                html.Append("</").Append(name).Append('>');
            }

            return;
        }

        var open = OpeningTag().Match(tag);
        if (!open.Success)
        {
            // Not a tag we recognise (<script>, <br>, <3, "a < b" …) — show it as text.
            AppendEscaped(tag, html);
            return;
        }

        var tagName = open.Groups["name"].Value.ToLowerInvariant();

        if (!AllowedTags.Contains(tagName))
        {
            AppendEscaped(tag, html);
            return;
        }

        if (openTags.Count >= MaxNestingDepth)
        {
            errors.Add(new SanitizationError(
                "text.nesting_too_deep",
                $"Tags must not be nested deeper than {MaxNestingDepth} levels.",
                position));
            return;
        }

        if (tagName == "a" && openTags.Any(t => t.Name == "a" && !t.Demoted))
        {
            errors.Add(new SanitizationError(
                "text.nested_anchor",
                "A link cannot be nested inside another link.",
                position));
            return;
        }

        var attributeText = open.Groups["attrs"].Value;

        var selfClosing = open.Groups["selfclose"].Success;

        if (!TryRenderAttributes(tagName, attributeText, out var rendered))
        {
            // Bad attributes make the tag untrusted; degrade it to text rather than dropping data,
            // and remember it so its closing tag is degraded the same way.
            AppendEscaped(tag, html);

            if (!selfClosing)
            {
                openTags.Push(new OpenTag(tagName, Demoted: true));
            }

            return;
        }

        html.Append('<').Append(tagName).Append(rendered).Append('>');

        if (selfClosing)
        {
            html.Append("</").Append(tagName).Append('>');
        }
        else
        {
            openTags.Push(new OpenTag(tagName, Demoted: false));
        }
    }

    /// <summary>An allowed tag that has been opened; <see cref="Demoted"/> when it was escaped to text.</summary>
    private sealed record OpenTag(string Name, bool Demoted);

    /// <summary>
    /// Rebuilds the attribute list from scratch instead of copying the user's. Anything not on the
    /// allowlist simply never reaches the output, and values are re-escaped, so crafted quoting
    /// cannot break out of the attribute.
    /// </summary>
    private static bool TryRenderAttributes(string tagName, string attributeText, [NotNullWhen(true)] out string? rendered)
    {
        rendered = null;

        var hasAttributes = !string.IsNullOrWhiteSpace(attributeText);

        if (tagName != "a")
        {
            // <code>, <i>, <strong> take no attributes at all.
            if (hasAttributes)
            {
                return false;
            }

            rendered = string.Empty;
            return true;
        }

        if (!hasAttributes)
        {
            return false; // <a> without href is pointless and usually a probe.
        }

        string? href = null;
        string? title = null;

        // Remove each recognised attribute from a scratch copy. Whatever is left must be
        // whitespace — otherwise the tag carried something we did not understand, such as the
        // unquoted `onclick=alert(1)`, and the whole tag is demoted to text.
        var leftovers = new StringBuilder(attributeText);

        foreach (Match attribute in Attribute().Matches(attributeText).OrderByDescending(m => m.Index))
        {
            leftovers.Remove(attribute.Index, attribute.Length);

            var name = attribute.Groups["name"].Value.ToLowerInvariant();

            if (!AllowedAnchorAttributes.Contains(name))
            {
                return false;
            }

            // Decoding before validating is deliberate: `jav&#x09;ascript:` must be seen as the
            // scheme it really is, so that the scheme allowlist below can reject it.
            var value = WebUtility.HtmlDecode(attribute.Groups["value"].Value);

            switch (name)
            {
                case "href":
                    if (href is not null || !TryNormalizeHref(value, out href))
                    {
                        return false;
                    }

                    break;
                case "title":
                    if (title is not null)
                    {
                        return false;
                    }

                    title = value.Length > 256 ? value[..256] : value;
                    break;
            }
        }

        if (leftovers.ToString().Any(c => !char.IsWhiteSpace(c)))
        {
            return false;
        }

        if (href is null)
        {
            return false;
        }

        var builder = new StringBuilder(" href=\"").Append(EscapeAttribute(href)).Append('"');

        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append(" title=\"").Append(EscapeAttribute(title)).Append('"');
        }

        // Defence in depth for stored links pointing at third-party sites.
        builder.Append(" rel=\"nofollow noopener noreferrer\" target=\"_blank\"");

        rendered = builder.ToString();
        return true;
    }

    private static bool TryNormalizeHref(string value, [NotNullWhen(true)] out string? href)
    {
        href = null;

        var candidate = value.Trim();

        if (candidate.Length is 0 or > 2048)
        {
            return false;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !AllowedLinkSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        href = uri.AbsoluteUri;
        return true;
    }

    private static void AppendText(ReadOnlySpan<char> text, StringBuilder html, StringBuilder plain)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '<':
                    html.Append("&lt;");
                    break;
                case '>':
                    html.Append("&gt;");
                    break;
                case '&':
                    html.Append("&amp;");
                    break;
                case '"':
                    html.Append("&quot;");
                    break;
                case '\'':
                    html.Append("&#39;");
                    break;
                default:
                    html.Append(c);
                    break;
            }

            plain.Append(c);
        }
    }

    private static void AppendEscaped(string text, StringBuilder html)
    {
        // Disallowed markup is shown, not executed — and it is not added to the plain-text
        // projection, so search results are not polluted with the user's failed <script> attempts.
        foreach (var c in text)
        {
            html.Append(c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }
    }

    private static string EscapeAttribute(string value) =>
        value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    private static bool IsWellFormedXhtml(string html, out string? error)
    {
        error = null;

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            ConformanceLevel = ConformanceLevel.Fragment,
            IgnoreWhitespace = false,
            MaxCharactersInDocument = MaxRawLength * 4,
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader($"<r>{html}</r>"), settings);
            while (reader.Read())
            {
                // Reading to the end is the check.
            }

            return true;
        }
        catch (XmlException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Matches anything that looks like a tag: <c>&lt;…&gt;</c> with no nested angle brackets.</summary>
    [GeneratedRegex("<[^<>]*>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex TagToken();

    [GeneratedRegex(
        @"^<\s*(?<name>[A-Za-z][A-Za-z0-9]*)(?<attrs>(?:\s[^<>]*?)?)\s*(?<selfclose>/)?>$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex OpeningTag();

    [GeneratedRegex(
        @"^<\s*/\s*(?<name>[A-Za-z][A-Za-z0-9]*)\s*>$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex ClosingTag();

    [GeneratedRegex(
        "\\s+(?<name>[A-Za-z][A-Za-z0-9-]*)\\s*=\\s*\"(?<value>[^\"<>]*)\"",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex Attribute();
}
