using FluentValidation;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts.Commands.UpdateProfile;

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("User Name is required.")
            .Length(UserName.MinLength, UserName.MaxLength)
                .WithMessage($"User Name must be {UserName.MinLength}–{UserName.MaxLength} characters long.")
            .Matches(UserName.Pattern)
                .WithMessage("User Name may contain only latin letters and digits.");

        RuleFor(x => x.HomePage)
            .MaximumLength(HomePageUrl.MaxLength)
            .Must(HomePageUrl.IsValid)
                .WithMessage("Home page must be an absolute http or https URL.")
            .When(x => !string.IsNullOrWhiteSpace(x.HomePage));
    }
}
