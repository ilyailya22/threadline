using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>Write-side access to users: guests identified by what they typed, and accounts.</summary>
public interface IUserRepository
{
    /// <summary>Finds an existing identity for the (user name, e-mail) pair the visitor typed.</summary>
    Task<User?> FindAsync(UserName userName, EmailAddress email, CancellationToken cancellationToken = default);

    /// <summary>The account that owns an address, if one does.</summary>
    Task<User?> FindAccountByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);

    /// <summary>The account linked to a Google identity, if one is.</summary>
    Task<User?> FindAccountByGoogleSubjectAsync(string googleSubject, CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(User user);
}
