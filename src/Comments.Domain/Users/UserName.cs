using System.Text.RegularExpressions;
using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Users;

/// <summary>
/// User Name as specified by the assignment: latin letters and digits only.
/// </summary>
public sealed partial class UserName : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 64;

    /// <summary>Latin letters and digits only — no spaces, no punctuation, no Cyrillic.</summary>
    public const string Pattern = "^[A-Za-z0-9]{2,64}$";

    private UserName(string value) => Value = value;

    public string Value { get; }

    public static UserName Create(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;

        if (candidate.Length is < MinLength or > MaxLength)
        {
            throw new DomainException(
                $"User name must be between {MinLength} and {MaxLength} characters long.");
        }

        if (!Validator().IsMatch(candidate))
        {
            throw new DomainException("User name may contain only latin letters and digits.");
        }

        return new UserName(candidate);
    }

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Validator().IsMatch(value.Trim());

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        // User names are compared case-insensitively so "Anonym" and "anonym" are one person.
        yield return Value.ToUpperInvariant();
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Validator();
}
