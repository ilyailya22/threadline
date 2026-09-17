using System.Xml.Linq;
using Threadline.Comments.Application.Comments.Sanitization;
using Shouldly;

namespace Threadline.Comments.UnitTests.Comments.Sanitization;

/// <summary>
/// The sanitiser is the single place that decides what markup reaches a browser, so it is tested
/// against both the assignment's functional rules and a corpus of real XSS payloads.
/// </summary>
public sealed class CommentTextSanitizerTests
{
    private readonly CommentTextSanitizer _sanitizer = new();

    // ---------------------------------------------------------------- allowed tags

    [Theory]
    [InlineData("<i>italic</i>")]
    [InlineData("<strong>bold</strong>")]
    [InlineData("<code>var x = 1;</code>")]
    [InlineData("<strong>bold <i>and italic</i></strong>")]
    public void Keeps_allowed_tags(string input)
    {
        var result = _sanitizer.Sanitize(input);

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Errors.Select(e => e.Message)));
        result.Value!.Html.ShouldBe(input);
    }

    [Fact]
    public void Keeps_anchor_and_hardens_it()
    {
        var result = _sanitizer.Sanitize("""<a href="https://example.com" title="Example">link</a>""");

        result.IsValid.ShouldBeTrue();
        result.Value!.Html.ShouldBe(
            """<a href="https://example.com/" title="Example" rel="nofollow noopener noreferrer" target="_blank">link</a>""");
    }

    [Fact]
    public void Accepts_anchor_without_title_because_title_is_optional()
    {
        var result = _sanitizer.Sanitize("""<a href="https://example.com">x</a>""");

        result.IsValid.ShouldBeTrue();
        result.Value!.Html.ShouldContain("""href="https://example.com/" """.TrimEnd());
        result.Value.Html.ShouldNotContain("title=");
    }

    // ---------------------------------------------------------------- XSS corpus

    [Theory]
    [InlineData("<script>alert('xss')</script>")]
    [InlineData("<SCRIPT>alert(1)</SCRIPT>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<IMG SRC=\"javascript:alert(1);\">")]
    [InlineData("<svg/onload=alert(1)>")]
    [InlineData("<iframe src=\"https://evil.tld\"></iframe>")]
    [InlineData("<body onload=alert(1)>")]
    [InlineData("<div style=\"x:expression(alert(1))\">x</div>")]
    [InlineData("<object data=\"data:text/html;base64,PHNjcmlwdD4=\"></object>")]
    [InlineData("<math><mtext><style><img src=x onerror=alert(1)>")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">click</a>")]
    [InlineData("<a href=\"jav&#x09;ascript:alert(1)\">click</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">click</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">click</a>")]
    [InlineData("<i onclick=\"alert(1)\">hover</i>")]
    [InlineData("<strong onmouseover=alert(1)>x</strong>")]
    [InlineData("<a href=\"https://ok.tld\" onclick=alert(1)>x</a>")]
    public void Neutralises_xss_payloads(string payload)
    {
        var result = _sanitizer.Sanitize(payload + " trailing text");

        // Either the input is refused outright, or it survives only as inert escaped text.
        if (!result.IsValid)
        {
            return;
        }

        AssertOnlyInertMarkup(result.Value!.Html);
    }

    [Fact]
    public void Escapes_the_classic_nested_script_bypass()
    {
        var result = _sanitizer.Sanitize("<scr<script>ipt>alert(1)</scr</script>ipt>");

        if (result.IsValid)
        {
            AssertOnlyInertMarkup(result.Value!.Html);
        }
    }

    /// <summary>
    /// Asserts the security contract the way a browser sees it. Substring checks are the wrong
    /// tool here: <c>&amp;lt;img onerror=…&amp;gt;</c> legitimately <em>contains</em> the string
    /// "onerror" while being inert text on the page. What matters is the parsed tree — which
    /// elements and attributes actually exist after the browser reads the markup.
    /// </summary>
    private static void AssertOnlyInertMarkup(string html)
    {
        var root = XElement.Parse($"<r>{html}</r>", LoadOptions.PreserveWhitespace);

        foreach (var element in root.Descendants())
        {
            CommentTextSanitizer.AllowedTags.ShouldContain(
                element.Name.LocalName,
                $"Unexpected element <{element.Name.LocalName}> survived sanitisation of: {html}");

            foreach (var attribute in element.Attributes())
            {
                var name = attribute.Name.LocalName;

                name.ShouldBeOneOf(["href", "title", "rel", "target"]);
                name.ShouldNotStartWith("on", Case.Insensitive);

                if (name == "href")
                {
                    var uri = new Uri(attribute.Value, UriKind.Absolute);
                    uri.Scheme.ShouldBeOneOf([Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto]);
                }
            }
        }
    }

    // ---------------------------------------------------------------- escaping

    [Theory]
    [InlineData("a < b", "a &lt; b")]
    [InlineData("a > b", "a &gt; b")]
    [InlineData("Tom & Jerry", "Tom &amp; Jerry")]
    [InlineData("say \"hi\"", "say &quot;hi&quot;")]
    [InlineData("it's fine", "it&#39;s fine")]
    public void Escapes_special_characters_in_plain_runs(string input, string expected)
    {
        var result = _sanitizer.Sanitize(input);

        result.IsValid.ShouldBeTrue();
        result.Value!.Html.ShouldBe(expected);
    }

    [Fact]
    public void Plain_text_projection_drops_markup_but_keeps_words()
    {
        var result = _sanitizer.Sanitize("<strong>Hello</strong> <i>world</i>");

        result.IsValid.ShouldBeTrue();
        result.Value!.PlainText.ShouldBe("Hello world");
    }

    // ---------------------------------------------------------------- tag balancing

    [Fact]
    public void Rejects_unclosed_tag()
    {
        var result = _sanitizer.Sanitize("<strong>never closed");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "text.unclosed_tag");
    }

    [Fact]
    public void Rejects_crossed_tags()
    {
        var result = _sanitizer.Sanitize("<i><strong>crossed</i></strong>");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "text.unexpected_closing_tag");
    }

    [Fact]
    public void Rejects_closing_tag_without_opening_one()
    {
        var result = _sanitizer.Sanitize("orphan </strong>");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "text.unexpected_closing_tag");
    }

    [Fact]
    public void Rejects_nested_anchors()
    {
        var result = _sanitizer.Sanitize(
            """<a href="https://a.tld"><a href="https://b.tld">x</a></a>""");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "text.nested_anchor");
    }

    [Fact]
    public void Output_is_always_well_formed_xhtml()
    {
        var result = _sanitizer.Sanitize(
            """Mixed <strong>bold <i>and <a href="https://x.tld" title="t">link</a></i></strong> & entities < >""");

        result.IsValid.ShouldBeTrue();

        // Round-trips through an XML parser — the definition of "valid XHTML" in the assignment.
        var xml = XElement.Parse($"<r>{result.Value!.Html}</r>");
        xml.ShouldNotBeNull();
    }

    // ---------------------------------------------------------------- guards

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void Rejects_empty_text(string? input)
    {
        var result = _sanitizer.Sanitize(input);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "text.required");
    }

    [Fact]
    public void Rejects_text_over_the_length_limit()
    {
        var result = _sanitizer.Sanitize(new string('a', 20_001));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "text.too_long");
    }

    [Fact]
    public void Rejects_markup_that_is_only_tags_with_no_words()
    {
        var result = _sanitizer.Sanitize("<i></i>");

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Code == "text.required");
    }

    [Fact]
    public void Does_not_take_quadratic_time_on_adversarial_input()
    {
        var payload = string.Concat(Enumerable.Repeat("<strong>a</strong>", 1_000));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = _sanitizer.Sanitize(payload);
        started.Stop();

        result.IsValid.ShouldBeTrue();
        started.ElapsedMilliseconds.ShouldBeLessThan(1_000);
    }
}
