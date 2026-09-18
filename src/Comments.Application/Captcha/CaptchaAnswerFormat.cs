namespace Threadline.Comments.Application.Captcha;

/// <summary>
/// What a CAPTCHA answer may look like before it is checked — one definition for the validator, the
/// request model and the rules published to the client.
/// </summary>
/// <remarks>
/// Looser than <see cref="CaptchaCodeGenerator.Alphabet"/> on purpose: a user who types a character
/// that is not in the alphabet has simply answered wrongly, which the one-shot check reports.
/// </remarks>
public static class CaptchaAnswerFormat
{
    public const int MaxLength = 16;

    public const string Pattern = "^[A-Za-z0-9]{1,16}$";
}
