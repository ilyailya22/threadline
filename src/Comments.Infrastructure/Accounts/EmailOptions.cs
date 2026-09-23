namespace Threadline.Comments.Infrastructure.Accounts;

/// <summary>Where transactional e-mail goes, and who it comes from.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host. Empty means "no sender configured" — see <see cref="LoggingEmailSender"/>.</summary>
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 1025;

    public bool UseStartTls { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = "no-reply@threadline.local";

    public string FromName { get; set; } = "Threadline";

    /// <summary>
    /// The public address of the site, used to build the links inside the messages. Without it a
    /// confirmation link would point at whatever host the request happened to arrive on, which a
    /// caller can forge.
    /// </summary>
    public string PublicUrl { get; set; } = "http://localhost:8080";
}
