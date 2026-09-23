namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>
/// Who is signed in on this request, or nobody. Supplied by the API from the authentication
/// cookie, so the application layer can tell an account from a guest without touching
/// <c>HttpContext</c>.
/// </summary>
public interface ICurrentUser
{
    Guid? Id { get; }

    bool IsAuthenticated => Id is not null;
}
