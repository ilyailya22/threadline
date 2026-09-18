using Threadline.Comments.Application.Comments.Dtos;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetComment;

/// <summary>A single comment, or <see langword="null"/> if it does not exist.</summary>
public sealed record GetCommentQuery(Guid Id) : IRequest<CommentNodeDto?>;
