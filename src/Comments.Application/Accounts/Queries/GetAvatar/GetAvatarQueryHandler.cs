using MediatR;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;

namespace Threadline.Comments.Application.Accounts.Queries.GetAvatar;

public sealed class GetAvatarQueryHandler(
    IUserRepository users,
    IFileStorage storage) : IRequestHandler<GetAvatarQuery, AvatarContent>
{
    public async Task<AvatarContent> Handle(GetAvatarQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByIdAsync(request.AccountId, cancellationToken);

        if (account?.AvatarPath is not { } path)
        {
            // No uploaded picture: the client draws initials, or Google's picture if there is one.
            throw new NotFoundException("Avatar", request.AccountId);
        }

        var content = await storage.OpenReadAsync(path, cancellationToken)
            ?? throw new NotFoundException("Avatar", request.AccountId);

        return new AvatarContent(content, ProcessedImageFormat.DisplayContentType);
    }
}
