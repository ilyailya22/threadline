namespace Threadline.Comments.Application.Accounts;

/// <summary>
/// What a password has to be, defined once — the validator, the API's published rules and the
/// client's form all read it from here.
/// </summary>
/// <remarks>
/// Length and nothing else. Composition rules ("one capital, one digit, one symbol") push people
/// towards Passw0rd! and away from passphrases, which is the opposite of what they are for; NIST
/// SP 800-63B has said so since 2017.
/// </remarks>
public static class PasswordPolicy
{
    public const int MinLength = 10;

    /// <summary>Long enough for any passphrase, short enough that hashing stays cheap.</summary>
    public const int MaxLength = 256;
}
