using Threadline.Comments.Domain.Common;

namespace Threadline.Comments.Domain.Users;

/// <summary>
/// The person a comment belongs to — a guest who typed a name and an address, or someone with an
/// account.
/// </summary>
/// <remarks>
/// <para>
/// One entity rather than two, because a comment's author is a comment's author whichever of the
/// two left it, and a board where half the rows point at one table and half at another is a join
/// nobody enjoys. An account is a user that also carries credentials: a password hash, a Google
/// subject, or both.
/// </para>
/// <para>
/// Registering never adopts the rows guests left behind, even with the same address. Those rows are
/// the authors of comments that were written before anyone proved anything, and rewriting them to
/// point at a new account would put words in that account's mouth. What registering does do is
/// close the address: once an account owns it, a guest can no longer post under it — see
/// <see cref="IsRegistered"/> and the unique index behind it.
/// </para>
/// </remarks>
public sealed class User : Entity
{
    /// <summary>How long a confirmation link stays valid.</summary>
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromHours(24);

    private User()
    {
        // EF Core
    }

    private User(
        Guid id,
        UserName userName,
        EmailAddress email,
        HomePageUrl? homePage,
        bool isRegistered,
        DateTimeOffset createdAt)
        : base(id)
    {
        UserName = userName;
        Email = email;
        HomePage = homePage;
        IsRegistered = isRegistered;
        CreatedAt = createdAt;
        LastPostedAt = createdAt;
    }

    public UserName UserName { get; private set; } = null!;

    public EmailAddress Email { get; private set; } = null!;

    public HomePageUrl? HomePage { get; private set; }

    /// <summary>
    /// True once credentials exist. Persisted rather than derived, because the unique index that
    /// reserves an address for its owner is filtered on it.
    /// </summary>
    public bool IsRegistered { get; private set; }

    /// <summary>Opaque to the domain: the hashing scheme belongs to infrastructure.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>Google's stable identifier for the person, from the <c>sub</c> claim.</summary>
    public string? GoogleSubject { get; private set; }

    public DateTimeOffset? EmailConfirmedAt { get; private set; }

    public bool IsEmailConfirmed => EmailConfirmedAt is not null;

    /// <summary>
    /// Hash of the outstanding confirmation token. The token itself is only ever in the e-mail: a
    /// leaked database backup must not hand out working confirmation links.
    /// </summary>
    public string? ConfirmationTokenHash { get; private set; }

    public DateTimeOffset? ConfirmationTokenExpiresAt { get; private set; }

    /// <summary>Blob path of the uploaded avatar; <see langword="null"/> means initials or the Google picture.</summary>
    public string? AvatarPath { get; private set; }

    /// <summary>Profile picture as Google gave it, used until the person uploads one of their own.</summary>
    public string? ExternalAvatarUrl { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastPostedAt { get; private set; }

    /// <summary>A guest identity: a name and an address, with nothing proved.</summary>
    public static User Register(
        UserName userName,
        EmailAddress email,
        HomePageUrl? homePage,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), userName, email, homePage, isRegistered: false, now);

    /// <summary>An account created with an e-mail address and a password, not yet confirmed.</summary>
    public static User RegisterAccount(
        EmailAddress email,
        string passwordHash,
        UserName? userName,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User(Guid.CreateVersion7(now), userName ?? UserName.FromEmail(email), email, homePage: null, isRegistered: true, now)
        {
            PasswordHash = passwordHash,
        };
    }

    /// <summary>
    /// An account created by signing in with Google. The address needs no confirmation: Google has
    /// just asserted it, which is the whole point of the round trip.
    /// </summary>
    public static User RegisterGoogleAccount(
        EmailAddress email,
        string googleSubject,
        UserName? userName,
        string? avatarUrl,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSubject);

        return new User(Guid.CreateVersion7(now), userName ?? UserName.FromEmail(email), email, homePage: null, isRegistered: true, now)
        {
            GoogleSubject = googleSubject,
            ExternalAvatarUrl = avatarUrl,
            EmailConfirmedAt = now,
        };
    }

    /// <summary>
    /// Attaches Google to an account that already exists for the same address.
    /// </summary>
    /// <remarks>
    /// Someone who registered with a password and later presses "Continue with Google" is the same
    /// person, and Google has just confirmed the address — so this also confirms it here, rather
    /// than leaving them chasing an e-mail they no longer need.
    /// </remarks>
    public void LinkGoogle(string googleSubject, string? avatarUrl, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSubject);

        if (GoogleSubject is not null && GoogleSubject != googleSubject)
        {
            throw new DomainException("This account is already linked to a different Google account.");
        }

        GoogleSubject = googleSubject;
        IsRegistered = true;
        ExternalAvatarUrl ??= avatarUrl;
        EmailConfirmedAt ??= now;
        ClearConfirmationToken();
    }

    /// <summary>Records the hash of a freshly issued confirmation token.</summary>
    public void IssueConfirmationToken(string tokenHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (IsEmailConfirmed)
        {
            throw new DomainException("This address is already confirmed.");
        }

        ConfirmationTokenHash = tokenHash;
        ConfirmationTokenExpiresAt = now.Add(ConfirmationWindow);
    }

    /// <summary>
    /// Confirms the address if the token matches and has not expired; the token is single-use.
    /// </summary>
    public bool ConfirmEmail(string tokenHash, DateTimeOffset now)
    {
        if (IsEmailConfirmed)
        {
            // Following the link twice is something people do; the second time is not an error.
            return true;
        }

        if (ConfirmationTokenHash is null
            || ConfirmationTokenExpiresAt is null
            || ConfirmationTokenExpiresAt < now
            || !FixedTimeEquals(ConfirmationTokenHash, tokenHash))
        {
            return false;
        }

        EmailConfirmedAt = now;
        ClearConfirmationToken();

        return true;
    }

    public void ChangeUserName(UserName userName)
    {
        ArgumentNullException.ThrowIfNull(userName);

        UserName = userName;
    }

    /// <summary>
    /// Takes the home page a returning user typed. For a guest it is the only mutable profile
    /// field, and leaving it blank — the field is optional and easy to skip — must not wipe what we
    /// already know. Account settings clear it explicitly instead, through <see cref="SetHomePage"/>.
    /// </summary>
    public void UpdateHomePage(HomePageUrl? homePage)
    {
        if (homePage is not null)
        {
            HomePage = homePage;
        }
    }

    /// <summary>Sets the home page to exactly what was given, including nothing.</summary>
    public void SetHomePage(HomePageUrl? homePage) => HomePage = homePage;

    public void SetAvatar(string storagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        AvatarPath = storagePath;
    }

    /// <summary>Back to the Google picture, or to initials when there is none.</summary>
    public void ClearAvatar() => AvatarPath = null;

    /// <summary>Called by <see cref="Comments.Comment"/> whenever this user posts.</summary>
    internal void RecordPost(DateTimeOffset postedAt)
    {
        if (postedAt > LastPostedAt)
        {
            LastPostedAt = postedAt;
        }
    }

    private void ClearConfirmationToken()
    {
        ConfirmationTokenHash = null;
        ConfirmationTokenExpiresAt = null;
    }

    /// <summary>
    /// Compares two hashes without leaking, through timing, how much of one matches the other.
    /// </summary>
    private static bool FixedTimeEquals(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var difference = 0;

        for (var i = 0; i < left.Length; i++)
        {
            difference |= left[i] ^ right[i];
        }

        return difference == 0;
    }
}
