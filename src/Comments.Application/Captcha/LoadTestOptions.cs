namespace Threadline.Comments.Application.Captcha;

public sealed class LoadTestOptions
{
    public const string SectionName = "LoadTest";

    /// <summary>Must be explicitly true, and is additionally refused in the Production environment.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Answer that satisfies any challenge while the bypass is on. Has no default: leaving it empty
    /// disables the bypass, so a half-finished configuration fails closed.
    /// </summary>
    public string BypassAnswer { get; set; } = string.Empty;
}
