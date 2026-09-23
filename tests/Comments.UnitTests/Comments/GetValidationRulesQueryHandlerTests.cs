using System.Text.RegularExpressions;
using Threadline.Comments.Application.Comments.Commands.CreateComment;
using Threadline.Comments.Application.Comments.Queries.GetValidationRules;
using FluentValidation.TestHelper;
using Shouldly;

namespace Threadline.Comments.UnitTests.Comments;

/// <summary>
/// The published rules and the server's own validator must agree, or the client would accept what
/// the server rejects (or the reverse). These tests hold them to each other.
/// </summary>
public sealed class GetValidationRulesQueryHandlerTests
{
    private static readonly ValidationRulesDto Rules =
        new GetValidationRulesQueryHandler().Handle(new GetValidationRulesQuery(), CancellationToken.None).Result;

    private readonly CreateCommentCommandValidator _validator = new();

    [Theory]
    [InlineData("Anonym42", true)]
    [InlineData("A", false)]
    [InlineData("Анонім", false)]
    [InlineData("user name", false)]
    public void The_published_user_name_rule_matches_the_server_validator(string userName, bool valid)
    {
        Matches(Rules.UserName.Pattern!, userName).ShouldBe(valid);

        var result = _validator.TestValidate(Command() with { UserName = userName });

        (!result.Errors.Any(e => e.PropertyName == nameof(CreateCommentCommand.UserName))).ShouldBe(valid);
    }

    [Theory]
    [InlineData("anonym@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("a@b", false)]
    public void The_published_email_rule_matches_the_server_validator(string email, bool valid)
    {
        Matches(Rules.Email.Pattern!, email).ShouldBe(valid);

        var result = _validator.TestValidate(Command() with { Email = email });

        (!result.Errors.Any(e => e.PropertyName == nameof(CreateCommentCommand.Email))).ShouldBe(valid);
    }

    [Fact]
    public void Only_the_tags_the_assignment_allows_are_published() =>
        Rules.AllowedTags.Order().ShouldBe(["a", "code", "i", "strong"]);

    [Fact]
    public void Image_and_text_types_are_those_the_assignment_allows()
    {
        Rules.Attachments.ImageExtensions.ShouldBe([".jpg", ".jpeg", ".png", ".gif"], ignoreOrder: true);
        Rules.Attachments.TextExtensions.ShouldBe([".txt"]);
        Rules.Attachments.MaxTextFileBytes.ShouldBe(100 * 1024);
        (Rules.Attachments.MaxImageWidth, Rules.Attachments.MaxImageHeight).ShouldBe((320, 240));
    }

    private static bool Matches(string pattern, string value) =>
        Regex.IsMatch(value, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

    private static CreateCommentCommand Command() =>
        new(null, "Anonym", "anonym@example.com", null, "Hello", null, Guid.CreateVersion7(), "AB3K7", null);
}
