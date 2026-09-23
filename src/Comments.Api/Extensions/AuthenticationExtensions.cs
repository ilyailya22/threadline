using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Threadline.Comments.Application.Accounts.Dtos;

namespace Threadline.Comments.Api.Extensions;

/// <summary>Cookie sign-in, and Google when it is configured.</summary>
public static class AuthenticationExtensions
{
    public const string CookieName = "tl.auth";

    /// <summary>Where the Google round trip comes back to; registered with Google as the redirect URI.</summary>
    public const string GoogleCallbackPath = "/api/auth/google/callback";

    public static IServiceCollection AddAccountAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var google = configuration.GetSection("Authentication:Google");
        var clientId = google["ClientId"];
        var clientSecret = google["ClientSecret"];

        var builder = services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                // A session in a cookie, not a token in localStorage: the SPA and the API share an
                // origin, so there is nothing to read the cookie with and nothing for a script that
                // does get injected to steal.
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;

                // This is an API: an unauthenticated call gets a status code, not a redirect to a
                // login page that does not exist on the server.
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret))
        {
            builder.AddGoogle(options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.CallbackPath = GoogleCallbackPath;

                // The sign-in itself is ours; Google is only asked who this is. The ticket it
                // issues is never persisted as a session.
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.SaveTokens = false;
                // Google sends the profile picture in the userinfo payload, and nothing picks it
                // up by default.
                options.Events.OnCreatingTicket = context =>
                {
                    if (context.User.TryGetProperty("picture", out var picture)
                        && picture.GetString() is { Length: > 0 } url)
                    {
                        context.Identity?.AddClaim(new Claim(GoogleClaims.Picture, url));
                    }

                    return Task.CompletedTask;
                };
            });
        }

        return services;
    }

    /// <summary>True when the "Continue with Google" button has something to talk to.</summary>
    public static bool IsGoogleConfigured(this IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return !string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]);
    }

    /// <summary>The claims that make up a signed-in session.</summary>
    public static ClaimsPrincipal ToPrincipal(this AccountDto account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.Name, account.UserName),
                new Claim(ClaimTypes.Email, account.Email),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return new ClaimsPrincipal(identity);
    }
}

/// <summary>Claim types Google fills in that ASP.NET Core has no constant for.</summary>
public static class GoogleClaims
{
    public const string Picture = "urn:google:picture";
}
