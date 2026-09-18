using Threadline.Comments.Application.Comments.Dtos;
using Threadline.Comments.Application.Common.Models;
using MediatR;

namespace Threadline.Comments.Application.Comments.Queries.GetTopLevelComments;

/// <summary>
/// The main page: top-level comments only, 25 per page, sortable by User Name, E-mail or date,
/// ascending or descending, newest-first by default.
/// </summary>
public sealed record GetTopLevelCommentsQuery(
    CommentPageRequest Page,
    string? Search = null) : IRequest<PagedResult<CommentListItemDto>>;
