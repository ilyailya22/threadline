using System.Security.Cryptography;
using System.Text;

namespace Threadline.Comments.Application.Common.Security;

/// <summary>
/// Compares user-supplied strings against secrets in time that does not depend on where they differ.
/// </summary>
/// <remarks>
/// A CAPTCHA answer is not a secret worth a timing attack, but comparing user input in constant time
/// is a habit worth keeping uniform across a codebase — the day it is a token, nobody has to
/// remember to switch.
/// </remarks>
public static class ConstantTime
{
    public static bool AreEqual(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
}
