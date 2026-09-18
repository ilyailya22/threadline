using Threadline.Comments.Application.Comments.Dtos;
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
