using MediatR;
using Threadline.Comments.Application.Common.Abstractions;

namespace Threadline.Comments.Application.Accounts.Commands.ConfirmEmail;

public sealed class ConfirmEmailCommandHandler(
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IConfirmationTokens tokens,
    IDateTimeProvider clock) : IRequestHandler<ConfirmEmailCommand, bool>
{
    public async Task<bool> Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await users.FindByIdAsync(request.AccountId, cancellationToken) is not { IsRegistered: true } account)
        {
            return false;
        }

        if (!account.ConfirmEmail(tokens.HashOf(request.Token), clock.UtcNow))
        {
            return false;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
