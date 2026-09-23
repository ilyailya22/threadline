using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Accounts.Commands.UpdateProfile;

public sealed class UpdateProfileCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateProfileCommand, AccountDto>
{
    public async Task<AccountDto> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await CurrentAccount.LoadAsync(currentUser, users, cancellationToken);

        account.ChangeUserName(UserName.Create(request.UserName));
        account.SetHomePage(HomePageUrl.CreateOrNull(request.HomePage));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AccountMapper.ToDto(account);
    }
}
