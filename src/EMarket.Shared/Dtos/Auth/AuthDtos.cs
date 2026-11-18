using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Auth;

public sealed class RegisterRequest
{
    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Name, ErrorMessage = Messages.Invalid)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), EmailAddress(ErrorMessage = Messages.Invalid), StringLength(150, ErrorMessage = Messages.Invalid)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Phone, ErrorMessage = Messages.Invalid)]
    public string Phone { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Password, ErrorMessage = Messages.Invalid)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), Compare(nameof(Password), ErrorMessage = Messages.Invalid)]
    public string ConfirmPassword { get; set; } = "";

    [Range(typeof(bool), "true", "true", ErrorMessage = Messages.Required)]
    public bool AcceptTerms { get; set; }
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = Messages.Required), EmailAddress(ErrorMessage = Messages.Invalid)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = Messages.Required)]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; }
}

public sealed class RefreshRequest
{
    [Required(ErrorMessage = Messages.Required)]
    public string RefreshToken { get; set; } = "";
}

public sealed class EmailRequest
{
    [Required(ErrorMessage = Messages.Required), EmailAddress(ErrorMessage = Messages.Invalid)]
    public string Email { get; set; } = "";
}

public sealed class TokenRequest
{
    [Required(ErrorMessage = Messages.Required)]
    public string Token { get; set; } = "";
}

public sealed class ResetPasswordRequest
{
    [Required(ErrorMessage = Messages.Required)]
    public string Token { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Password, ErrorMessage = Messages.Invalid)]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), Compare(nameof(NewPassword), ErrorMessage = Messages.Invalid)]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class ChangePasswordRequest
{
    [Required(ErrorMessage = Messages.Required)]
    public string OldPassword { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Password, ErrorMessage = Messages.Invalid)]
    public string NewPassword { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), Compare(nameof(NewPassword), ErrorMessage = Messages.Invalid)]
    public string ConfirmPassword { get; set; } = "";
}

public sealed record TokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    Guid UserId,
    string FullName,
    bool EmailConfirmed,
    bool MustChangePassword,
    IReadOnlyList<string> Roles);
