using MediatR;
using Threadline.Comments.Application.Accounts.Dtos;
using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;

namespace Threadline.Comments.Application.Accounts.Commands.SetAvatar;

/// <summary>
/// Takes an uploaded picture through the same treatment as a comment's attachment: the bytes decide
/// what it is, the EXIF orientation is applied, and it is re-encoded — which is what strips
/// metadata and anything hiding after the image payload.
/// </summary>
/// <remarks>
/// Square-ish and small: an avatar is shown at 32 pixels in a table and about 96 in settings, so
/// anything larger is bytes nobody sees. It is fitted rather than cropped, for the same reason the
/// assignment gives for comment images — cropping decides for the person which half of their face
/// matters.
/// </remarks>
public sealed class SetAvatarCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IFileTypeSniffer sniffer,
    IImageProcessor images,
    IFileStorage storage,
    IDateTimeProvider clock) : IRequestHandler<SetAvatarCommand, AccountDto>
{
    public const int MaxSize = 256;

    private const string FieldName = "file";

    public async Task<AccountDto> Handle(SetAvatarCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await CurrentAccount.LoadAsync(currentUser, users, cancellationToken);
        var upload = request.Upload;

        if (upload.Length <= 0)
        {
            throw new InputValidationException(FieldName, "The uploaded file is empty.");
        }

        if (upload.Length > Domain.Comments.Attachment.MaxImageUploadBytes)
        {
            throw new InputValidationException(
                FieldName,
                $"The file must not exceed {Domain.Comments.Attachment.MaxImageUploadBytes / (1024 * 1024)} MB.");
        }

        var header = new byte[FileTypeSniffer.HeaderSize];
        var read = await ReadHeaderAsync(upload.Content, header, cancellationToken);

        if (sniffer.Detect(header.AsSpan(0, read)) is not { Kind: Domain.Comments.AttachmentKind.Image })
        {
            throw new InputValidationException(FieldName, "An avatar must be a JPG, GIF or PNG image.");
        }

        if (upload.Content.CanSeek)
        {
            upload.Content.Position = 0;
        }

        var processed = await images.DownscaleAsync(upload.Content, MaxSize, MaxSize, cancellationToken);

        // The path carries the account id and the moment, so a new avatar never lands on the URL a
        // browser or a CDN already holds.
        var now = clock.UtcNow;
        var path = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"avatars/{account.Id:N}/{now:yyyyMMddHHmmssfff}{ProcessedImageFormat.DisplayExtension}");

        using (var content = new MemoryStream(processed.Content, writable: false))
        {
            await storage.SaveAsync(path, content, ProcessedImageFormat.DisplayContentType, cancellationToken);
        }

        account.SetAvatar(path);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return AccountMapper.ToDto(account);
    }

    private static async Task<int> ReadHeaderAsync(
        Stream content,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var total = 0;

        while (total < buffer.Length)
        {
            var read = await content.ReadAsync(buffer.AsMemory(total), cancellationToken);

            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
