using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts.Commands.SignIn;

/// <summary>
/// Returns the account when the password is right, and <see langword="null"/> when it is not —
/// without saying which half was wrong.
/// </summary>
/// <remarks>
/// An address with no account still costs a password verification. Skipping it would make "no such
/// address" measurably faster than "wrong password", which is an account-enumeration oracle that
/// needs no successful login at all, just a stopwatch.
/// </remarks>
public sealed class SignInCommandHandler(
    IUserRepository users,
    IPasswordHasher passwords) : IRequestHandler<SignInCommand, AccountDto?>
{
    private const string Decoy = "there-is-no-account-here";

    public async Task<AccountDto?> Handle(SignInCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = EmailAddress.IsValid(request.Email)
            ? await users.FindAccountByEmailAsync(EmailAddress.Create(request.Email), cancellationToken)
            : null;

        if (account?.PasswordHash is { } hash)
        {
            return passwords.Verify(hash, request.Password) ? AccountMapper.ToDto(account) : null;
        }

        // No account, or one that has only ever signed in with Google: spend the time anyway.
        passwords.Verify(passwords.Hash(Decoy), request.Password);

        return null;
    }
}
