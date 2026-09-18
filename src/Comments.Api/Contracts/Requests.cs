using System.ComponentModel.DataAnnotations;

namespace Threadline.Comments.Api.Contracts;

/// <summary>Body of the live-preview request.</summary>
public sealed class PreviewRequest
{
    [Required]
    [StringLength(20_000, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// The comment form, as posted.
/// </summary>
/// <remarks>
/// The data annotations here are a cheap first gate that rejects obvious junk before it reaches the
/// pipeline. They are not the authority: the real rules live in
/// <c>CreateCommentCommandValidator</c> and in the domain value objects, and those run on every
/// request regardless of what this model says.
/// </remarks>
public sealed class CreateCommentRequest
{
    [Required]
    [StringLength(64, MinimumLength = 2)]
    public string UserName { get; set; } = string.Empty;

    [Required]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [StringLength(2048)]
    public string? HomePage { get; set; }

    [Required]
    [StringLength(20_000, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;

    /// <summary>The comment being replied to; omit to start a new thread.</summary>
    public Guid? ParentId { get; set; }

    [Required]
    public Guid CaptchaId { get; set; }

    [Required]
    [StringLength(16, MinimumLength = 1)]
    public string CaptchaAnswer { get; set; } = string.Empty;

    /// <summary>Optional image (JPG/GIF/PNG) or text file (TXT).</summary>
    public IFormFile? File { get; set; }
}
