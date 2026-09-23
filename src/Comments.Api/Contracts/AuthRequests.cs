using System.ComponentModel.DataAnnotations;
using Threadline.Comments.Application.Accounts;
using Threadline.Comments.Domain.Users;

// Threadline.Comments.Domain is aliased below where UserName would otherwise mean the property.

namespace Threadline.Comments.Api.Contracts;

/// <summary>Sign-up: an address and a password, and nothing else to fill in.</summary>
/// <remarks>
/// No nickname field. It is derived from the address and can be changed in settings afterwards —
/// one fewer decision between wanting an account and having one.
/// </remarks>
public sealed class RegisterRequest
{
    [Required]
    [StringLength(EmailAddress.MaxLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(PasswordPolicy.MaxLength, MinimumLength = PasswordPolicy.MinLength)]
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    [Required]
    [StringLength(EmailAddress.MaxLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(PasswordPolicy.MaxLength)]
    public string Password { get; set; } = string.Empty;
}

public sealed class ConfirmEmailRequest
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [StringLength(128)]
    public string Token { get; set; } = string.Empty;
}

/// <summary>Account settings: the parts a person may change about themselves.</summary>
public sealed class UpdateProfileRequest
{
    [Required]
    [StringLength(Domain.Users.UserName.MaxLength, MinimumLength = Domain.Users.UserName.MinLength)]
    public string UserName { get; set; } = string.Empty;

    [StringLength(HomePageUrl.MaxLength)]
    public string? HomePage { get; set; }
}
