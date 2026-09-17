using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Users;

/// <summary>
/// Home page — the one optional field on the form. Validated with <see cref="Uri"/> rather than a
/// regex, and restricted to http/https so that a stored <c>javascript:</c> or <c>data:</c> URL can
/// never become an XSS vector when the profile link is rendered.
/// </summary>
public sealed class HomePageUrl : ValueObject
{
    public const int MaxLength = 2048;

    private static readonly string[] AllowedSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps];

    private HomePageUrl(string value) => Value = value;

    public string Value { get; }

    /// <summary>Returns <see langword="null"/> for an absent value — the field is optional.</summary>
    public static HomePageUrl? CreateOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();

        if (candidate.Length > MaxLength)
        {
            throw new DomainException($"Home page URL must not exceed {MaxLength} characters.");
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainException("Home page must be an absolute http or https URL.");
        }

        return new HomePageUrl(uri.ToString());
    }

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true; // optional
        }

        return value.Trim().Length <= MaxLength
            && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase);
    }

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }
}
