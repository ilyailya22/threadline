using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Common.Abstractions;

namespace Threadline.Comments.Application.Accounts.Commands.RemoveAvatar;

public sealed class RemoveAvatarCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IUnitOfWork unitOfWork) : IRequestHandler<RemoveAvatarCommand, AccountDto>
{
    public async Task<AccountDto> Handle(RemoveAvatarCommand request, CancellationToken cancellationToken)
    {
        var account = await CurrentAccount.LoadAsync(currentUser, users, cancellationToken);

        // The blob is left where it is. Deleting it would break the caches and the pages that still
        // point at that URL, and a lifecycle rule on the container is the right place to reclaim it.
        account.ClearAvatar();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AccountMapper.ToDto(account);
    }
}
