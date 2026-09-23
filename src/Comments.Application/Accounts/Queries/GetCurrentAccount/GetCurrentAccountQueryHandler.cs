using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Common.Abstractions;

namespace Threadline.Comments.Application.Accounts.Queries.GetCurrentAccount;

public sealed class GetCurrentAccountQueryHandler(
    ICurrentUser currentUser,
    IUserRepository users) : IRequestHandler<GetCurrentAccountQuery, AccountDto?>
{
    public async Task<AccountDto?> Handle(GetCurrentAccountQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.Id is not { } id)
        {
            return null;
        }

        var account = await users.FindByIdAsync(id, cancellationToken);

        // A cookie can outlive the row it points at — a deleted account, a restored backup.
        return account is { IsRegistered: true } ? AccountMapper.ToDto(account) : null;
    }
}
