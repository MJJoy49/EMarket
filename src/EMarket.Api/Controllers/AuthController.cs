using System.Security.Claims;
using EMarket.BLL.Identity;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EMarket.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private Guid UserId => Guid.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    [AllowAnonymous, HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        await auth.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, new { message = Messages.RegistrationDone });
    }

    [AllowAnonymous, EnableRateLimiting("login"), HttpPost("login")]
    public Task<TokenResponse> Login(LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, Ip, ct);

    [AllowAnonymous, HttpPost("refresh")]
    public Task<TokenResponse> Refresh(RefreshRequest request, CancellationToken ct) => auth.RefreshAsync(request.RefreshToken, Ip, ct);

    [AllowAnonymous, HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [AllowAnonymous, HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(TokenRequest request, CancellationToken ct)
    {
        await auth.VerifyEmailAsync(request.Token, ct);
        return NoContent();
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    {
        await auth.ResendVerificationAsync(UserId, ct);
        return NoContent();
    }

    [AllowAnonymous, HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct)
    {
        await auth.ForgotPasswordAsync(request.Email, ct);
        return Ok(new { message = Messages.ResetSent });
    }

    [AllowAnonymous, HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        await auth.ResetPasswordAsync(request, ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    public Task<TokenResponse> ChangePassword(ChangePasswordRequest request, CancellationToken ct) =>
        auth.ChangePasswordAsync(UserId, request, Ip, ct);
}
