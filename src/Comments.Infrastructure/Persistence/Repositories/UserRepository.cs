using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Threadline.Comments.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> FindAsync(
        UserName userName,
        EmailAddress email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(email);

        // Hits UX_Users_UserName_Email. The comparison is case-insensitive because the column
        // collation is, which matches how UserName and EmailAddress define their own equality.
        // Tracked: the user is modified (last activity) and becomes the Author of a new comment. An
        // untracked instance would be seen as a new entity and inserted a second time.
        return context.Users.AsTracking().FirstOrDefaultAsync(
            u => u.UserName == userName && u.Email == email,
            cancellationToken);
    }

    public Task<User?> FindAccountByEmailAsync(
        EmailAddress email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        // Hits UX_Users_Email_Registered, which holds exactly the accounts.
        return context.Users.AsTracking().FirstOrDefaultAsync(
            u => u.IsRegistered && u.Email == email,
            cancellationToken);
    }

    public Task<User?> FindAccountByGoogleSubjectAsync(
        string googleSubject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSubject);

        return context.Users.AsTracking().FirstOrDefaultAsync(
            u => u.GoogleSubject == googleSubject,
            cancellationToken);
    }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public void Add(User user) => context.Users.Add(user);
}
