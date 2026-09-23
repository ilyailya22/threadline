using Microsoft.AspNetCore.Identity;
using Threadline.Comments.Application.Common.Abstractions;
using Threadline.Comments.Domain.Users;

namespace Threadline.Comments.Infrastructure.Accounts;

/// <summary>
/// ASP.NET Core Identity's password hasher, and nothing else from Identity.
/// </summary>
/// <remarks>
/// <para>
/// Hashing passwords by hand is how people end up with SHA-256 and no salt. This is PBKDF2 with
/// HMAC-SHA512, a per-password salt and a work factor Microsoft raises as hardware does — the
/// format carries its own version, so an old hash keeps verifying while new ones get the current
/// parameters.
/// </para>
/// <para>
/// Taking the hasher without the rest of Identity is deliberate: the stores, the entity base
/// classes and the token providers would reach into the domain, and the domain is where the
/// account invariants live.
/// </para>
/// </remarks>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly PasswordHasher<User> Hasher = new();

    /// <summary>Only the hash is passed in, so this stands in for the user the API wants.</summary>
    private static readonly User Anyone = User.Register(
        UserName.Create("placeholder"),
        EmailAddress.Create("placeholder@example.com"),
        homePage: null,
        DateTimeOffset.UnixEpoch);

    public string Hash(string password) => Hasher.HashPassword(Anyone, password);

    public bool Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(Anyone, hash, password) is not PasswordVerificationResult.Failed;
}
