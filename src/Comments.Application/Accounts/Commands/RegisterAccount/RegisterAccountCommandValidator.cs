using FluentValidation;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts.Commands.RegisterAccount;

public sealed class RegisterAccountCommandValidator : AbstractValidator<RegisterAccountCommand>
{
    public RegisterAccountCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("E-mail is required.")
            .MaximumLength(EmailAddress.MaxLength)
            .Matches(EmailAddress.Pattern).WithMessage("E-mail is not a valid address.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(PasswordPolicy.MinLength)
                .WithMessage($"Password must be at least {PasswordPolicy.MinLength} characters long.")
            .MaximumLength(PasswordPolicy.MaxLength);
    }
}
