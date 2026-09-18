namespace Threadline.Comments.Application.Comments.Sanitization;

/// <summary>Successfully sanitised text, in both of the forms the system stores.</summary>
public sealed record SanitizedText(string Html, string PlainText);
