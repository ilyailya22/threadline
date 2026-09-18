namespace Threadline.Comments.Application.Comments.Dtos;

/// <summary>Author fields as they appear next to a comment.</summary>
public sealed record AuthorDto(
    Guid Id,
    string UserName,
    string Email,
    string? HomePage);
