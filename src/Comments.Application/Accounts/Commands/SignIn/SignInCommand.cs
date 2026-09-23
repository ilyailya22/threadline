using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;

namespace Threadline.Comments.Application.Accounts.Commands.SignIn;

/// <summary>Checks a password. The cookie is the API's job; this only says who it is for.</summary>
public sealed record SignInCommand(string Email, string Password) : IRequest<AccountDto?>;
