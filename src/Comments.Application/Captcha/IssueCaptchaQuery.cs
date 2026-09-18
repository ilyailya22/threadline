using MediatR;

namespace Threadline.Comments.Application.Captcha;

/// <summary>Issues a fresh CAPTCHA challenge for the comment form.</summary>
public sealed record IssueCaptchaQuery : IRequest<CaptchaChallenge>;

public sealed class IssueCaptchaQueryHandler(ICaptchaService captcha)
    : IRequestHandler<IssueCaptchaQuery, CaptchaChallenge>
{
    public Task<CaptchaChallenge> Handle(IssueCaptchaQuery request, CancellationToken cancellationToken) =>
        captcha.IssueAsync(cancellationToken);
}
