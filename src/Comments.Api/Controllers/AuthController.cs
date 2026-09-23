using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Threadline.Comments.Api.Contracts;
using Threadline.Comments.Api.Extensions;
using Threadline.Comments.Application.Accounts.Commands.ConfirmEmail;
using Threadline.Comments.Application.Accounts.Commands.RegisterAccount;
using Threadline.Comments.Application.Accounts.Commands.ResendConfirmation;
using Threadline.Comments.Application.Accounts.Commands.SignIn;
using Threadline.Comments.Application.Accounts.Commands.SignInWithGoogle;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Accounts.Queries.GetCurrentAccount;

namespace Threadline.Comments.Api.Controllers;

/// <summary>Registering, signing in and signing out.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISender sender, IConfiguration configuration) : ControllerBase
{
    /// <summary>
    /// Which ways in exist. Google needs credentials this deployment may not have, and a button
    /// that leads to a 404 is worse than no button.
    /// </summary>
    [HttpGet("providers")]
    [ProducesResponseType<SignInProvidersDto>(StatusCodes.Status200OK)]
    public ActionResult<SignInProvidersDto> Providers() =>
        new SignInProvidersDto(configuration.IsGoogleConfigured());

    /// <summary>Who is signed in; 204 for nobody.</summary>
    [HttpGet("me")]
    [ProducesResponseType<AccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var account = await sender.Send(new GetCurrentAccountQuery(), cancellationToken);

        return account is null ? NoContent() : Ok(account);
    }

    /// <summary>Creates an account and sends the confirmation link.</summary>
    /// <remarks>
    /// The session starts immediately: an unconfirmed account can read and post, and the interface
    /// says the address is unconfirmed. Locking someone out until they find an e-mail is how you
    /// lose them at the one moment they were willing to sign up.
    /// </remarks>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AccountDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await sender.Send(
            new RegisterAccountCommand(request.Email, request.Password),
            cancellationToken);

        await SignInAsync(account);

        return Created(string.Empty, account);
    }

    /// <summary>Signs in with an e-mail address and a password.</summary>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<AccountDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await sender.Send(
            new SignInCommand(request.Email, request.Password),
            cancellationToken);

        if (account is null)
        {
            // One message for both halves. Which of the two was wrong is not the caller's business.
            return Problem(
                title: "E-mail or password is incorrect.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        await SignInAsync(account);

        return Ok(account);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return NoContent();
    }

    /// <summary>Follows a confirmation link.</summary>
    [HttpPost("confirm")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmEmailRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var confirmed = await sender.Send(
            new ConfirmEmailCommand(request.Id, request.Token),
            cancellationToken);

        return confirmed
            ? NoContent()
            : Problem(
                title: "This confirmation link is no longer valid. Ask for a new one.",
                statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>Sends another confirmation link to the signed-in account.</summary>
    [HttpPost("confirm/resend")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResendConfirmation(CancellationToken cancellationToken)
    {
        await sender.Send(new ResendConfirmationCommand(), cancellationToken);

        return NoContent();
    }

    /// <summary>Starts the Google round trip.</summary>
    [HttpGet("google")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public IActionResult Google([FromQuery] string? returnUrl)
    {
        // A local path only: an open redirect here would let a phishing page borrow the sign-in.
        var target = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";

        return Challenge(
            new AuthenticationProperties
            {
                RedirectUri = $"/api/auth/google/complete?returnUrl={Uri.EscapeDataString(target)}",
            },
            GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>Where the visitor lands once Google has identified them.</summary>
    [HttpGet("google/complete")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> GoogleComplete(
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email);

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            return Redirect("/sign-in?error=google");
        }

        var account = await sender.Send(
            new SignInWithGoogleCommand(subject, email, User.FindFirstValue(GoogleClaims.Picture)),
            cancellationToken);

        // Replaces the ticket Google's handler wrote with our own, so the cookie carries this
        // application's account id rather than a Google subject.
        await SignInAsync(account);

        return Redirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    private Task SignInAsync(AccountDto account) =>
        HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            account.ToPrincipal(),
            new AuthenticationProperties { IsPersistent = true });
}

/// <param name="Google">True when "Continue with Google" has something to talk to.</param>
public sealed record SignInProvidersDto(bool Google);
