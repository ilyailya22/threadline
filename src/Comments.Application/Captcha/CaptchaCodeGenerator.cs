using System.Security.Cryptography;

namespace Threadline.Comments.Application.Captcha;

/// <summary>
/// Generates the challenge text.
/// </summary>
/// <remarks>
/// Characters that are easy to confuse in a distorted image — 0/O, 1/I/l, 5/S, 2/Z — are left out.
/// Rejecting a human who read the image correctly is a worse failure than a slightly smaller
/// alphabet: 27^5 is still over 14 million combinations against a one-shot, rate-limited challenge.
/// </remarks>
public sealed class CaptchaCodeGenerator : ICaptchaCodeGenerator
{
    /// <summary>The characters a challenge is made of — also what a valid answer may contain.</summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRTUVWXY346789";

    public const int Length = 5;

    public string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);
}
