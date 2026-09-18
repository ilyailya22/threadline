namespace Threadline.Comments.Application.Comments.Dtos;

/// <summary>Server-rendered preview of a message, used by the "preview without reload" feature.</summary>
public sealed record CommentPreviewDto(string TextHtml, string TextPlain);
