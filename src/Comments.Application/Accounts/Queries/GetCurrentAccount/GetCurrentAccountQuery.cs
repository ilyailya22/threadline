using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;

namespace Threadline.Comments.Application.Accounts.Queries.GetCurrentAccount;

/// <summary>Who is signed in, or nothing.</summary>
public sealed record GetCurrentAccountQuery : IRequest<AccountDto?>;
