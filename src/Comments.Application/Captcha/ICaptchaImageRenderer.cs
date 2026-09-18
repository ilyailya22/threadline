namespace Threadline.Comments.Application.Captcha;

/// <summary>Renders the challenge text into a distorted PNG.</summary>
public interface ICaptchaImageRenderer
{
    byte[] Render(string code);
}
