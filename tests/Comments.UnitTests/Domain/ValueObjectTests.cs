using Threadline.Comments.Domain.Common;
using Threadline.Comments.Domain.Users;
using Shouldly;

namespace Threadline.Comments.UnitTests.Domain;

/// <summary>
/// The form's field rules, asserted where they are defined. These same constants drive the
/// FluentValidation rules and, through <c>/api/validation-rules</c>, the Angular validators — so a
/// test here covers all three.
/// </summary>
public sealed class UserNameTests
{
    [Theory]
    [InlineData("Anonym")]
    [InlineData("Rum8")]
    [InlineData("a1")]
    [InlineData("ABCdef0123456789")]
    public void Accepts_latin_letters_and_digits(string value)
    {
        UserName.Create(value).Value.ShouldBe(value);
        UserName.IsValid(value).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]                 // too short
    [InlineData("has space")]
    [InlineData("punctuation!")]
    [InlineData("under_score")]
    [InlineData("Кириллица")]         // the assignment says latin only
    [InlineData("emoji😀")]
    public void Rejects_anything_else(string value)
    {
        UserName.IsValid(value).ShouldBeFalse();
        Should.Throw<DomainException>(() => UserName.Create(value));
    }

    [Fact]
    public void Rejects_a_name_over_the_length_limit() => Should.Throw<DomainException>(() => UserName.Create(new string('a', UserName.MaxLength + 1)));

    [Fact]
    public void Is_compared_case_insensitively_so_one_person_is_one_user() => UserName.Create("Anonym").ShouldBe(UserName.Create("anonym"));

    [Fact]
    public void Surrounding_whitespace_is_trimmed_rather_than_rejected() => UserName.Create("  Anonym  ").Value.ShouldBe("Anonym");
}

public sealed class EmailAddressTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@sub.example.co.uk")]
    [InlineData("user+tag@example.io")]
    [InlineData("u@e.dev")]
    public void Accepts_valid_addresses(string value) => EmailAddress.Create(value).Value.ShouldBe(value);

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@example.com")]
    [InlineData("user@")]
    [InlineData("user@example")]       // no TLD
    [InlineData("user@.com")]
    [InlineData("user name@example.com")]
    [InlineData("user@exam ple.com")]
    public void Rejects_invalid_addresses(string value) => EmailAddress.IsValid(value).ShouldBeFalse();

    [Fact]
    public void Rejects_an_address_over_the_rfc_length_limit()
    {
        var local = new string('a', 250);

        EmailAddress.IsValid($"{local}@example.com").ShouldBeFalse();
    }

    /// <summary>
    /// The pattern is bounded everywhere, so the classic catastrophic-backtracking e-mail regex
    /// attack finds nothing to exploit. The 200 ms match timeout is the backstop.
    /// </summary>
    [Fact]
    public void Is_not_vulnerable_to_catastrophic_backtracking()
    {
        var payload = new string('a', 200) + "@" + new string('a', 40) + "!";

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        EmailAddress.IsValid(payload).ShouldBeFalse();
        stopwatch.Stop();

        stopwatch.ElapsedMilliseconds.ShouldBeLessThan(500);
    }

    [Fact]
    public void Exposes_its_domain() => EmailAddress.Create("user@example.com").Domain.ShouldBe("example.com");
}

public sealed class HomePageUrlTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Is_optional(string? value)
    {
        HomePageUrl.CreateOrNull(value).ShouldBeNull();
        HomePageUrl.IsValid(value).ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?query=1")]
    public void Accepts_absolute_http_urls(string value) => HomePageUrl.CreateOrNull(value).ShouldNotBeNull();

    /// <summary>
    /// The scheme allowlist is a security control, not a formatting preference: a stored
    /// <c>javascript:</c> URL becomes an XSS vector the moment the profile link is rendered.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]        // not absolute
    [InlineData("//example.com")]      // protocol-relative
    public void Rejects_anything_that_is_not_http_or_https(string value)
    {
        HomePageUrl.IsValid(value).ShouldBeFalse();
        Should.Throw<DomainException>(() => HomePageUrl.CreateOrNull(value));
    }
}
