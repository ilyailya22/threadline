using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Comments.Sanitization;
using Threadline.Comments.Application.Common.Exceptions;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.PreviewComment;

/// <summary>
/// Renders what a message will look like once posted — the assignment's "пред просмотр сообщения,
/// без перезагрузки страницы".
/// </summary>
/// <remarks>
/// The preview is produced by the <em>same</em> sanitiser that will process the real submission, so
/// what the user sees in the preview is exactly what gets stored. Rendering the preview purely in
/// the browser would be faster but would let the two disagree — and the one that disagrees silently
/// is always the security-relevant one.
/// </remarks>
public sealed record PreviewCommentQuery(string Text) : IRequest<CommentPreviewDto>;

public sealed class PreviewCommentQueryHandler(ICommentTextSanitizer sanitizer)
    : IRequestHandler<PreviewCommentQuery, CommentPreviewDto>
{
    public Task<CommentPreviewDto> Handle(PreviewCommentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = sanitizer.Sanitize(request.Text);

        if (!result.IsValid)
        {
            throw new InputValidationException("text", [.. result.Errors.Select(e => e.Message)]);
        }

        return Task.FromResult(new CommentPreviewDto(result.Value!.Html, result.Value.PlainText));
    }
}
