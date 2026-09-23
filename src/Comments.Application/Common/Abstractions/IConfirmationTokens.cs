namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>A single-use token: the half that travels in an e-mail, and the half that is stored.</summary>
/// <param name="Token">Goes into the link. Never stored.</param>
/// <param name="Hash">Goes into the database. Useless to whoever reads it.</param>
public readonly record struct ConfirmationToken(string Token, string Hash);

/// <summary>Issues confirmation tokens and recomputes their hashes when one comes back.</summary>
public interface IConfirmationTokens
{
    ConfirmationToken Issue();

    string HashOf(string token);
}
