using System.Text.RegularExpressions;
using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Users;

/// <summary>
/// E-mail in "format email" as the assignment requires.
/// </summary>
/// <remarks>
/// The pattern is deliberately a pragmatic subset of RFC 5322 rather than the full grammar:
/// the full grammar accepts addresses no mail server would route, and the classic
/// "one regex to rule them all" is a well known catastrophic-backtracking hazard. Every quantifier
/// here is bounded and the regex has a 200 ms match timeout, so it cannot be used for ReDoS.
/// </remarks>
public sealed partial class EmailAddress : ValueObject
{
    public const int MaxLength = 254; // RFC 5321 §4.5.3.1.3

    public const string Pattern =
        @"^[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[A-Za-z0-9!#$%&'*+/=?^_`{|}~-]+)*"
        + @"@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$";

    private EmailAddress(string value) => Value = value;

    public string Value { get; }

    public string Domain => Value[(Value.IndexOf('@', StringComparison.Ordinal) + 1)..];

    public static EmailAddress Create(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;

        if (candidate.Length is 0 or > MaxLength)
        {
            throw new DomainException($"E-mail must be between 1 and {MaxLength} characters long.");
        }

        if (!Validator().IsMatch(candidate))
        {
            throw new DomainException("E-mail is not a valid address.");
        }

        return new EmailAddress(candidate);
    }

    public static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= MaxLength
        && Validator().IsMatch(value.Trim());

    public override string ToString() => Value;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value.ToUpperInvariant();
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Validator();
}
