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
    /// Called when a known user posts again. The home page is the only mutable field: the user may
    /// have got a new one since last time, but clearing it accidentally (the field is optional and
    /// easy to leave blank) must not wipe what we already know.
    /// </summary>
    public void RecordActivity(HomePageUrl? homePage, DateTimeOffset now)
    {
        if (homePage is not null)
        {
            HomePage = homePage;
        }

        if (now > LastPostedAt)
        {
            LastPostedAt = now;
        }
    }
}
