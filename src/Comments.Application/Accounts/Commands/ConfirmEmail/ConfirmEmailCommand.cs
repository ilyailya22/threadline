using MediatR;

namespace Threadline.Comments.Application.Accounts.Commands.ConfirmEmail;

/// <summary>Follows a confirmation link. Returns whether it was still good.</summary>
public sealed record ConfirmEmailCommand(Guid AccountId, string Token) : IRequest<bool>;
