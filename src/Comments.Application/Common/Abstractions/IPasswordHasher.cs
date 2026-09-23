namespace Threadline.Comments.Application.Common.Abstractions;

/// <summary>Turns a password into something safe to store, and checks one against it.</summary>
/// <remarks>
/// The scheme — algorithm, work factor, salt, encoding — is an infrastructure decision that will
/// change as hardware does. The application only ever holds the opaque result.
/// </remarks>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);
}
