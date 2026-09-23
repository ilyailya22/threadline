using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;

namespace Threadline.Comments.Application.Accounts.Commands.RegisterAccount;

/// <param name="Email">Becomes the account's address and, squeezed into the allowed alphabet, its first nickname.</param>
/// <param name="Password">Checked against <see cref="PasswordPolicy"/> and never stored as given.</param>
public sealed record RegisterAccountCommand(string Email, string Password) : IRequest<AccountDto>;
