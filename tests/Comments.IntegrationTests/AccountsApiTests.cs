using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.IntegrationTests.Infrastructure;
using Shouldly;

namespace Threadline.Comments.IntegrationTests;

/// <summary>
/// Accounts end to end: registering, signing in, and what changes about posting once someone has.
/// </summary>
[Collection(IntegrationTestSuite.Name)]
public sealed class AccountsApiTests(CommentsApiFactory factory) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();

        // Cookies, because the session is one: a client that drops them cannot stay signed in.
        _client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
        });
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Registering_derives_the_nickname_from_the_address_and_signs_the_person_in()
    {
        var account = await RegisterAsync("j.doe+news@example.com");

        account.UserName.ShouldBe("jdoenews");
        account.Email.ShouldBe("j.doe+news@example.com");
        account.IsEmailConfirmed.ShouldBeFalse();
        account.HasPassword.ShouldBeTrue();

        // The cookie came back with the registration, so this needs no further sign-in.
        var me = await _client.GetFromJsonAsync<AccountDto>("/api/auth/me", Json);
        me!.Id.ShouldBe(account.Id);
    }

    [Fact]
    public async Task An_address_can_only_be_registered_once()
    {
        await RegisterAsync("taken@example.com");

        using var again = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new { email = "taken@example.com", password = "correct horse battery" });

        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_without_saying_which_half_was_wrong()
    {
        await RegisterAsync("member@example.com");

        using var wrongPassword = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "member@example.com", password = "not the password" });

        using var noSuchAccount = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "nobody@example.com", password = "not the password" });

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        noSuchAccount.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var first = await wrongPassword.Content.ReadAsStringAsync();
        var second = await noSuchAccount.Content.ReadAsStringAsync();

        first.ShouldBe(second);
    }

    [Fact]
    public async Task Following_the_confirmation_link_confirms_the_address()
    {
        var account = await RegisterAsync("confirm.me@example.com");
        var token = factory.Emails.TokenFor("confirm.me@example.com");

        token.ShouldNotBeNull();

        using var confirmed = await _client.PostAsJsonAsync(
            "/api/auth/confirm",
            new { id = account.Id, token });

        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var me = await _client.GetFromJsonAsync<AccountDto>("/api/auth/me", Json);
        me!.IsEmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_stale_confirmation_token_is_refused()
    {
        var account = await RegisterAsync("stale@example.com");

        using var response = await _client.PostAsJsonAsync(
            "/api/auth/confirm",
            new { id = account.Id, token = "not-the-token" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The point of an account, from the comment form's side: no name, no address, no CAPTCHA.
    /// </summary>
    [Fact]
    public async Task An_account_posts_without_a_captcha_and_under_its_own_name()
    {
        var account = await RegisterAsync("author@example.com");

        using var content = new MultipartFormDataContent { { new StringContent("Posted as myself."), "text" } };
        using var response = await _client.PostAsync("/api/comments", content);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        var created = await response.Content.ReadFromJsonAsync<CreateCommentResultDto>(Json);
        var thread = await _client.GetFromJsonAsync<CommentThreadDto>($"/api/comments/{created!.RootId}/thread", Json);

        var author = thread!.Nodes.Single().Author;
        author.Id.ShouldBe(account.Id);
        author.UserName.ShouldBe("author");
        author.Email.ShouldBe("author@example.com");
    }

    /// <summary>
    /// The board prints the author's address next to every comment, so an address an account owns
    /// has to be closed to guests — otherwise anyone can wear it.
    /// </summary>
    [Fact]
    public async Task A_guest_cannot_post_under_an_address_that_belongs_to_an_account()
    {
        await RegisterAsync("owner@example.com");

        using var signedOut = await _client.PostAsync("/api/auth/logout", content: null);
        signedOut.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var captcha = await _client.GetAsync("/api/captcha");
        captcha.EnsureSuccessStatusCode();

        using var content = new MultipartFormDataContent
        {
            { new StringContent("Hello, it is me, the owner."), "text" },
            { new StringContent("Impostor"), "userName" },
            { new StringContent("owner@example.com"), "email" },
            { new StringContent(captcha.Headers.GetValues("X-Captcha-Id").Single()), "captchaId" },
            { new StringContent(CommentsApiFactory.CaptchaAnswer), "captchaAnswer" },
        };

        using var response = await _client.PostAsync("/api/comments", content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Sign in");
    }

    [Fact]
    public async Task Settings_change_the_nickname_that_appears_on_comments()
    {
        await RegisterAsync("rename.me@example.com");

        using var updated = await _client.PutAsJsonAsync(
            "/api/accounts/me",
            new { userName = "Renamed", homePage = "https://example.com" });

        updated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var account = await updated.Content.ReadFromJsonAsync<AccountDto>(Json);
        account!.UserName.ShouldBe("Renamed");
        account.HomePage.ShouldBe("https://example.com/");
    }

    [Fact]
    public async Task An_avatar_is_stored_downscaled_and_served_back()
    {
        var account = await RegisterAsync("avatar@example.com");

        var image = new ByteArrayContent(Png(600, 400));
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        using var form = new MultipartFormDataContent { { image, "file", "face.png" } };
        using var uploaded = await _client.PostAsync("/api/accounts/me/avatar", form);

        uploaded.StatusCode.ShouldBe(HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());

        var withAvatar = await uploaded.Content.ReadFromJsonAsync<AccountDto>(Json);
        withAvatar!.AvatarUrl.ShouldBe($"/api/accounts/{account.Id}/avatar");

        using var served = await _client.GetAsync(withAvatar.AvatarUrl);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");

        using var removed = await _client.DeleteAsync("/api/accounts/me/avatar");
        var withoutAvatar = await removed.Content.ReadFromJsonAsync<AccountDto>(Json);
        withoutAvatar!.AvatarUrl.ShouldBeNull();
    }

    private async Task<AccountDto> RegisterAsync(string email)
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, password = "correct horse battery" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<AccountDto>(Json))!;
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);

        canvas.Clear(SkiaSharp.SKColors.SeaGreen);

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);

        return encoded.ToArray();
    }
}
