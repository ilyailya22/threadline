namespace Threadline.Comments.Application.Captcha;

/// <summary>Generates the challenge text: latin letters and digits, as the assignment requires.</summary>
public interface ICaptchaCodeGenerator
{
    string Generate();
}
