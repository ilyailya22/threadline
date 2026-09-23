using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts.Commands.SignInWithGoogle;

/// <summary>
/// Signs in with Google: matches the Google identity, else the address, else creates an account.
/// </summary>
/// <remarks>
/// Matching on the subject first matters — an address can change hands at Google, and the subject
/// cannot. The address is only used to recognise someone who registered here with a password and
/// is now taking the shortcut; in that case Google is attached to the account they already have
/// rather than a second one being made behind their back.
/// </remarks>
public sealed class SignInWithGoogleCommandHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : IRequestHandler<SignInWithGoogleCommand, AccountDto>
{
    public async Task<AccountDto> Handle(SignInWithGoogleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.UtcNow;
        var email = EmailAddress.Create(request.Email);

        var account = await users.FindAccountByGoogleSubjectAsync(request.Subject, cancellationToken);

        if (account is null)
        {
            account = await users.FindAccountByEmailAsync(email, cancellationToken);

            if (account is null)
            {
                account = User.RegisterGoogleAccount(email, request.Subject, userName: null, request.PictureUrl, now);
                users.Add(account);
            }
            else
            {
                account.LinkGoogle(request.Subject, request.PictureUrl, now);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AccountMapper.ToDto(account);
    }
}
