namespace Threadline.Comments.Application.Common.Models;

/// <summary>
/// Fields the top-level comment table can be sorted by. The assignment names exactly these three.
/// </summary>
/// <remarks>
/// Sorting is an enum, never a client-supplied column name. That is the structural defence against
/// SQL injection through an <c>ORDER BY</c> clause: there is no code path where user text becomes
/// part of a query, so there is nothing to escape and nothing to forget to escape.
/// </remarks>
public enum CommentSortField
{
    /// <summary>Date added — the default.</summary>
    CreatedAt = 0,
    UserName = 1,
    Email = 2,
}
