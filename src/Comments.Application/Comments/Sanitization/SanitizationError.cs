namespace Threadline.Comments.Application.Comments.Sanitization;

/// <summary>A single reason the submitted text was rejected.</summary>
/// <param name="Code">Stable machine-readable code — the Angular client maps it to a localised message.</param>
/// <param name="Message">Human-readable English description.</param>
/// <param name="Position">Zero-based offset in the original text, when known.</param>
public sealed record SanitizationError(string Code, string Message, int? Position = null);
