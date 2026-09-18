using MediatR;

namespace Threadline.Comments.Application.Captcha;

/// <summary>Issues a fresh CAPTCHA challenge for the comment form.</summary>
public sealed record IssueCaptchaQuery : IRequest<CaptchaChallenge>;
