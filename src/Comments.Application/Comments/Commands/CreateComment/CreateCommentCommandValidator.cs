using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;
using FluentValidation;

namespace Threadline.Comments.Application.Comments.Commands.CreateComment;

/// <summary>
/// Server-side validation of the form. The assignment asks for validation on both the client and
/// the server; these rules are the authoritative half.
/// </summary>
/// <remarks>
/// The patterns are not duplicated here — they come from the domain value objects, which are also
/// what the Angular validators are built from (the <c>/api/validation-rules</c> endpoint). One
/// definition, three enforcement points.
/// </remarks>
public sealed class CreateCommentCommandValidator : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentCommandValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("User Name is required.")
            .Length(UserName.MinLength, UserName.MaxLength)
                .WithMessage($"User Name must be {UserName.MinLength}–{UserName.MaxLength} characters long.")
            .Matches(UserName.Pattern)
                .WithMessage("User Name may contain only latin letters and digits.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-mail is required.")
            .MaximumLength(EmailAddress.MaxLength)
            .Matches(EmailAddress.Pattern).WithMessage("E-mail is not a valid address.");

        RuleFor(x => x.HomePage)
            .MaximumLength(HomePageUrl.MaxLength)
            .Must(HomePageUrl.IsValid)
                .WithMessage("Home page must be an absolute http or https URL.")
            .When(x => !string.IsNullOrWhiteSpace(x.HomePage));

        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Message text is required.")
            .MaximumLength(CommentBody.MaxHtmlLength);

        RuleFor(x => x.CaptchaId)
            .NotEmpty().WithMessage("CAPTCHA is required.");

        RuleFor(x => x.CaptchaAnswer)
            .NotEmpty().WithMessage("CAPTCHA is required.")
            .Matches(CaptchaAnswerFormat.Pattern)
                .WithMessage("CAPTCHA may contain only latin letters and digits.");
    }
}
