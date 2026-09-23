using MediatR;

namespace Threadline.Comments.Application.Accounts.Queries.GetAvatar;

/// <summary>The stored avatar of one account.</summary>
public sealed record GetAvatarQuery(Guid AccountId) : IRequest<AvatarContent>;

/// <param name="Content">The image bytes. The caller owns the stream.</param>
/// <param name="ContentType">Always the processor output format; the upload is re-encoded.</param>
public sealed record AvatarContent(Stream Content, string ContentType);
