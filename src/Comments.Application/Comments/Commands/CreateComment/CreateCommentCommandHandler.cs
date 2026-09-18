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
    IClientContext client,
    IDateTimeProvider clock) : IRequestHandler<CreateCommentCommand, CreateCommentResultDto>
{
    public async Task<CreateCommentResultDto> Handle(
        CreateCommentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. CAPTCHA first: it is the cheapest check that stops the most traffic, and validating it
        //    before touching the database keeps a bot flood off the connection pool.
        if (!await captcha.ValidateAsync(request.CaptchaId, request.CaptchaAnswer, cancellationToken))
        {
            throw new InputValidationException(
                "captchaAnswer",
                "The CAPTCHA answer is incorrect or has expired. Please try again.");
        }

        // 2. Sanitising happens before anything is written, so nothing unsafe can reach storage even
        //    if a later step fails.
        var sanitized = sanitizer.Sanitize(request.Text);

        if (!sanitized.IsValid)
        {
            throw new InputValidationException(
                "text",
                [.. sanitized.Errors.Select(e => e.Message)]);
        }

        var userName = UserName.Create(request.UserName);
        var email = EmailAddress.Create(request.Email);
        var homePage = HomePageUrl.CreateOrNull(request.HomePage);
        var now = clock.UtcNow;

        // 3. Resolve the parent before creating anything, so a reply to a deleted or bogus id fails
        //    cleanly instead of leaving an orphan.
        Comment? parent = null;

        if (request.ParentId is { } parentId)
        {
            parent = await comments.GetForReplyAsync(parentId, cancellationToken)
                ?? throw new NotFoundException("Comment", parentId);
        }

        var author = await users.FindAsync(userName, email, cancellationToken);

        if (author is null)
        {
            author = User.Register(userName, email, homePage, now);
            users.Add(author);
        }
        else
        {
            author.RecordActivity(homePage, now);
        }

        var body = CommentBody.FromSanitized(sanitized.Value!.Html, sanitized.Value.PlainText);

        var fingerprint = ClientFingerprint.Create(client.IpHash, client.UserAgent, client.ClientId);

        var staged = request.Attachment is null
            ? null
            : await attachments.StageAsync(request.Attachment, cancellationToken);

        IEnumerable<Attachment>? files = staged is null ? null : [staged];

        var comment = parent is null
            ? Comment.CreateRoot(author, body, fingerprint, now, files)
            : Comment.CreateReply(author, parent, body, fingerprint, now, files);

        comments.Add(comment);

        // 4. One transaction: the comment, its attachment and the outbox row that will trigger
        //    indexing, thumbnailing and the live update. Either all of it happened or none of it did.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateCommentResultDto(
            comment.Id,
            comment.RootId,
            comment.ParentId,
            comment.CreatedAt,
            comment.Body.Html);
    }
}
