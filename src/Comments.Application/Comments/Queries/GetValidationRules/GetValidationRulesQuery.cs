using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetValidationRules;

/// <summary>
/// The validation rules the server enforces, published so the client can enforce the same ones.
/// </summary>
/// <remarks>
/// The assignment asks for validation on both the client and the server. The usual way to do that
/// is to write each regex twice and watch them drift apart; here the patterns and limits have one
/// definition — the domain value objects — and the Angular form builds its validators from this.
/// The server never trusts the client's check; it just stops them disagreeing.
/// </remarks>
public sealed record GetValidationRulesQuery : IRequest<ValidationRulesDto>;
