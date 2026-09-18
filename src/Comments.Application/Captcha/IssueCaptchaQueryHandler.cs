using MediatR;

namespace Threadline.Comments.Application.Captcha;

public sealed class IssueCaptchaQueryHandler(ICaptchaService captcha)
    : IRequestHandler<IssueCaptchaQuery, CaptchaChallenge>
{
    public Task<CaptchaChallenge> Handle(IssueCaptchaQuery request, CancellationToken cancellationToken) =>
        captcha.IssueAsync(cancellationToken);
}
