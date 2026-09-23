using Threadline.Comments.Application.Attachments;
using Threadline.Comments.Application.Captcha;
using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Comments.Sanitization;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Application.Common.Exceptions;
using Threadline.Comments.Domain.Comments;
using Threadline.Comments.Domain.Users;
using MediatR;

namespace Threadline.Comments.Application.Comments.Commands.CreateComment;

/// <summary>
/// The write path, and the place where the performance target of the assignment is met or lost.
/// </summary>
/// <remarks>
/// What this handler does: verify the CAPTCHA, sanitise the text, resolve or create the author,
/// stage the attachment, insert one row (plus one outbox row) and return. What it deliberately does
/// <em>not</em> do: index into Elasticsearch, resize images, invalidate caches, or notify connected
/// browsers. All of that hangs off <c>CommentCreatedDomainEvent</c> and runs on a worker, so the
/// latency a user sees is one database round trip and the write path does not fan out.
/// </remarks>
public sealed class CreateCommentCommandHandler(
    IUserRepository users,
    ICommentRepository comments,
    IUnitOfWork unitOfWork,
    ICommentTextSanitizer sanitizer,
    ICaptchaService captcha,
    IAttachmentIntakeService attachments,
    IAttachmentDtoMapper attachmentMapper,
    IClientContext client,
    IDateTimeProvider clock) : IRequestHandler<CreateCommentCommand, CreateCommentResultDto>
{
    public async Task<CreateCommentResultDto> Handle(
        CreateCommentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // CAPTCHA first: it is the cheapest check that stops the most traffic, and validating it
        // before touching the database keeps a bot flood off the connection pool. A signed-in
        // account has already proved it is a person, and to it the challenge is only friction.
        if (request.AuthorId is null)
        {
            await EnsureCaptchaSolvedAsync(request, cancellationToken);
        }

        // Sanitising happens before anything is written, so nothing unsafe can reach storage even if
        // a later step fails.
        var text = sanitizer.SanitizeOrThrow(request.Text);
        var now = clock.UtcNow;

        // The parent is resolved before anything is created, so a reply to a bogus id fails cleanly
        // instead of leaving an orphan.
        var parent = await FindParentAsync(request.ParentId, cancellationToken);
        var author = await ResolveAuthorAsync(request, now, cancellationToken);

        var body = CommentBody.FromSanitized(text.Html, text.PlainText);
        var fingerprint = ClientFingerprint.Create(client.IpHash, client.UserAgent, client.ClientId);
        var files = await StageAttachmentAsync(request.Attachment, cancellationToken);

        var comment = parent is null
            ? Comment.CreateRoot(author, body, fingerprint, now, files)
            : Comment.CreateReply(author, parent, body, fingerprint, now, files);

        comments.Add(comment);

        // One transaction: the comment, its attachment and the outbox row that will trigger
        // indexing, thumbnailing and the live update. Either all of it happened or none of it did.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateCommentResultDto(
            comment.Id,
            comment.RootId,
            comment.ParentId,
            comment.CreatedAt,
            comment.Body.Html,
            [.. comment.Attachments.Select(attachmentMapper.ToDto)]);
    }

    private async Task EnsureCaptchaSolvedAsync(CreateCommentCommand request, CancellationToken cancellationToken)
    {
        if (!await captcha.ValidateAsync(request.CaptchaId, request.CaptchaAnswer, cancellationToken))
        {
            throw new InputValidationException(
                "captchaAnswer",
                "The CAPTCHA answer is incorrect or has expired. Please try again.");
        }
    }

    private async Task<Comment?> FindParentAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is not { } id)
        {
            return null;
        }

        return await comments.GetForReplyAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Comment), id);
    }

    /// <summary>A visitor is identified by the (user name, e-mail) pair; a new pair is a new user.</summary>
    private async Task<User> ResolveAuthorAsync(
        CreateCommentCommand request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (request.AuthorId is { } accountId)
        {
            return await users.FindByIdAsync(accountId, cancellationToken) is { IsRegistered: true } account
                ? account
                : throw new NotFoundException("Account", accountId);
        }

        var userName = UserName.Create(request.UserName);
        var email = EmailAddress.Create(request.Email);
        var homePage = HomePageUrl.CreateOrNull(request.HomePage);

        // An address that belongs to an account is not available to guests. Without this, anyone
        // could type a registered person's address and post as them — the board shows the address
        // next to every comment, so the impersonation would be convincing.
        if (await users.FindAccountByEmailAsync(email, cancellationToken) is not null)
        {
            throw new InputValidationException(
                "email",
                "This e-mail belongs to an account. Sign in to post with it.");
        }

        var author = await users.FindAsync(userName, email, cancellationToken);

        if (author is null)
        {
            author = User.Register(userName, email, homePage, now);
            users.Add(author);
        }
        else
        {
            author.UpdateHomePage(homePage);
        }

        return author;
    }

    private async Task<IReadOnlyList<Attachment>> StageAttachmentAsync(
        AttachmentUpload? upload,
        CancellationToken cancellationToken) =>
        upload is null ? [] : [await attachments.StageAsync(upload, cancellationToken)];
}
