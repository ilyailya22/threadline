namespace Threadline.Comments.Application.Captcha;

/// <summary>A challenge handed to the browser: an opaque id plus the PNG the user has to read.</summary>
public sealed record CaptchaChallenge(Guid Id, byte[] ImagePng, DateTimeOffset ExpiresAt);
