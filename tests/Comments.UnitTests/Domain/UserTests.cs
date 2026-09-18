using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;
using Shouldly;

namespace Threadline.Comments.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_home_page_replaces_the_old_one()
    {
        var user = Register(HomePageUrl.CreateOrNull("https://old.example/"));

        user.UpdateHomePage(HomePageUrl.CreateOrNull("https://new.example/"));

        user.HomePage!.Value.ShouldBe("https://new.example/");
    }

    [Fact]
    public void Leaving_the_home_page_blank_keeps_the_one_already_known()
    {
        var user = Register(HomePageUrl.CreateOrNull("https://old.example/"));

        user.UpdateHomePage(null);

        user.HomePage!.Value.ShouldBe("https://old.example/");
    }

    [Fact]
    public void Posting_a_comment_records_when_the_user_last_posted()
    {
        var user = Register(homePage: null);
        var later = Now.AddHours(1);

        Comment.CreateRoot(
            user,
            CommentBody.FromSanitized("Hi", "Hi"),
            ClientFingerprint.Create(new string('a', 64), userAgent: null, clientId: null),
            later);

        user.LastPostedAt.ShouldBe(later);
    }

    private static User Register(HomePageUrl? homePage) =>
        User.Register(UserName.Create("Anonym"), EmailAddress.Create("anonym@example.com"), homePage, Now);
}
