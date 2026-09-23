using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts.Commands.RegisterAccount;

/// <summary>Creates an account and sends the confirmation link.</summary>
/// <remarks>
/// The address is taken, or it is not: there is no "we may have sent you an e-mail" ambiguity
/// here. Hiding whether an address is registered is a real technique, but it is only worth
/// anything if every other path hides it too — and this application publishes comments with the
/// author's address on them, so the secret does not exist to keep.
/// </remarks>
public sealed class RegisterAccountCommandHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwords,
    IConfirmationTokens tokens,
    IAccountEmails emails,
    IDateTimeProvider clock) : IRequestHandler<RegisterAccountCommand, AccountDto>
{
    public async Task<AccountDto> Handle(RegisterAccountCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = EmailAddress.Create(request.Email);

        if (await users.FindAccountByEmailAsync(email, cancellationToken) is not null)
        {
            throw new InputValidationException("email", "An account with this e-mail already exists.");
        }

        var now = clock.UtcNow;
        var account = User.RegisterAccount(email, passwords.Hash(request.Password), userName: null, now);

        var token = tokens.Issue();
        account.IssueConfirmationToken(token.Hash, now);

        users.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit: an e-mail cannot be un-sent, so it must never go out for a registration
        // that then failed to save.
        await emails.SendConfirmationAsync(account, token.Token, cancellationToken);

        return AccountMapper.ToDto(account);
    }
}
