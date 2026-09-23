using MediatR;

namespace Threadline.Comments.Application.Accounts.Commands.ResendConfirmation;

/// <summary>Issues a fresh confirmation link for the signed-in account.</summary>
public sealed record ResendConfirmationCommand : IRequest;
