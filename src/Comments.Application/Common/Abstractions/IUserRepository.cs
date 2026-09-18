using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Common.Abstractions;

public interface IUserRepository
{
    /// <summary>Finds an existing identity for the (user name, e-mail) pair the visitor typed.</summary>
    Task<User?> FindAsync(UserName userName, EmailAddress email, CancellationToken cancellationToken = default);

    void Add(User user);
}
