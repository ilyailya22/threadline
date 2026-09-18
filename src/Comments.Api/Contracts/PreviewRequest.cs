using System.ComponentModel.DataAnnotations;
using Threadline.Comments.Domain.Comments;

namespace Threadline.Comments.Api.Contracts;

/// <summary>Body of the live-preview request.</summary>
public sealed class PreviewRequest
{
    [Required]
    [StringLength(CommentBody.MaxHtmlLength, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}
