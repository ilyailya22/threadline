using MediatR;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;

namespace Threadline.Comments.Application.Accounts.Commands.ResendConfirmation;

/// <summary>
/// A confirmation link expires in a day and e-mail goes astray, so the one thing a person in that
/// state needs is another link.
/// </summary>
public sealed class ResendConfirmationCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IConfirmationTokens tokens,
    IAccountEmails emails,
    IDateTimeProvider clock) : IRequestHandler<ResendConfirmationCommand>
{
    public async Task Handle(ResendConfirmationCommand request, CancellationToken cancellationToken)
    {
        var id = currentUser.Id ?? throw new NotFoundException("Account", Guid.Empty);

        if (await users.FindByIdAsync(id, cancellationToken) is not { IsRegistered: true } account)
        {
            throw new NotFoundException("Account", id);
        }

        if (account.IsEmailConfirmed)
        {
            return;
        }

        var token = tokens.Issue();
        account.IssueConfirmationToken(token.Hash, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await emails.SendConfirmationAsync(account, token.Token, cancellationToken);
    }
}
