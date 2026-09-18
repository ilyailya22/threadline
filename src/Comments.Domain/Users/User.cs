using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Users;

/// <summary>
/// The person who left a comment. The assignment asks to store "data that helps identify the
/// client", so a user is identified by the (user name, e-mail) pair they type; the request-level
/// fingerprint (hashed IP, user agent, client id) is stored per comment, not here, because the same
/// person may post from several devices.
/// </summary>
public sealed class User : Entity
{
    private User()
    {
        // EF Core
    }

    private User(Guid id, UserName userName, EmailAddress email, HomePageUrl? homePage, DateTimeOffset createdAt)
        : base(id)
    {
        UserName = userName;
        Email = email;
        HomePage = homePage;
        CreatedAt = createdAt;
        LastPostedAt = createdAt;
    }

    public UserName UserName { get; private set; } = null!;

    public EmailAddress Email { get; private set; } = null!;

    public HomePageUrl? HomePage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastPostedAt { get; private set; }

    public static User Register(
        UserName userName,
        EmailAddress email,
        HomePageUrl? homePage,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), userName, email, homePage, now);

    /// <summary>
    /// Takes the home page a returning user typed. It is the only mutable profile field: the user may
    /// have a new one since last time, but leaving it blank (the field is optional and easy to skip)
    /// must not wipe what we already know.
    /// </summary>
    public void UpdateHomePage(HomePageUrl? homePage)
    {
        if (homePage is not null)
        {
            HomePage = homePage;
        }
    }

    /// <summary>Called by <see cref="Comments.Comment"/> whenever this user posts.</summary>
    internal void RecordPost(DateTimeOffset postedAt)
    {
        if (postedAt > LastPostedAt)
        {
            LastPostedAt = postedAt;
        }
    }
}
