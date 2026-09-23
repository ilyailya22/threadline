using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;

namespace Threadline.Comments.Application.Accounts.Commands.UpdateProfile;

/// <param name="UserName">The nickname shown on comments.</param>
/// <param name="HomePage">Cleared when empty — in settings, blank means "remove it".</param>
public sealed record UpdateProfileCommand(string UserName, string? HomePage) : IRequest<AccountDto>;
