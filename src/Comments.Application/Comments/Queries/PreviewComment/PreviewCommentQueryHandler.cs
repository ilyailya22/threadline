using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Comments.Sanitization;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.PreviewComment;

public sealed class PreviewCommentQueryHandler(ICommentTextSanitizer sanitizer)
    : IRequestHandler<PreviewCommentQuery, CommentPreviewDto>
{
    public Task<CommentPreviewDto> Handle(PreviewCommentQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = sanitizer.SanitizeOrThrow(request.Text);

        return Task.FromResult(new CommentPreviewDto(text.Html, text.PlainText));
    }
}
