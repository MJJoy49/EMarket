using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EMarket.BLL.Identity;

public sealed record AccessToken(string Value, DateTime ExpiresAt);

/// <summary>Implemented by the API host, which owns the signing key.</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(User user, IReadOnlyList<string> roles);
}

public interface IAuthService
{
    Task RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<TokenResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default);
    Task<TokenResponse> RefreshAsync(string refreshToken, string? ip, CancellationToken ct = default);
    Task LogoutAsync(string refreshToken, CancellationToken ct = default);
    Task VerifyEmailAsync(string token, CancellationToken ct = default);
    Task ResendVerificationAsync(Guid userId, CancellationToken ct = default);
    Task ForgotPasswordAsync(string email, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<TokenResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string? ip, CancellationToken ct = default);
    Task<bool> IsSessionValidAsync(Guid userId, string securityStamp, CancellationToken ct = default);
}

public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher<User> hasher,
    IAccessTokenIssuer issuer,
    ISettingsReader settings,
    IClock clock,
    IMemoryCache cache) : IAuthService
{
    private const string VerifyPurpose = "VerifyEmail";
    private const string ResetPurpose = "ResetPassword";
    private const int MaxResendsPerHour = 3;

    public async Task RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            throw new AppException(409, Messages.EmailTaken);
        }

        EnsureStrong(request.Password);
        var buyerRole = await db.AspNetRoles.FirstAsync(r => r.Name == Roles.Buyer, ct);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            FullName = request.FullName.Trim(),
            Email = email,
            Phone = request.Phone.Trim(),
            SecurityStamp = NewStamp(),
            Status = "Active",
            PasswordHash = "",
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.Roles.Add(buyerRole);
        db.Users.Add(user);

        db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Type = "UserRegistered",
            Title = "Welcome to EMarket",
            Message = "Your account is ready. Please verify your email to start ordering.",
        });
        db.AuditLogs.Add(new AuditLog { UserId = user.Id, Action = "TermsAccepted", EntityName = "User", EntityId = user.Id });
        QueueVerificationEmail(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, string? ip, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || user.IsDeleted || user.Status == "Deleted")
        {
            throw new AppException(401, Messages.WrongLogin);
        }

        if (user.Status == "Blocked")
        {
            throw new AppException(401, Messages.Blocked);
        }

        var lockMinutes = await settings.GetIntAsync("lockoutMinutes", 15, ct);
        if (user.LockoutEnd > clock.UtcNow)
        {
            throw new AppException(401, string.Format(Messages.Locked, lockMinutes));
        }

        var check = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (check == PasswordVerificationResult.Failed)
        {
            await RecordFailedLoginAsync(user, lockMinutes, ct);
            throw new AppException(401, Messages.WrongLogin);
        }

        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        }

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        var lifetime = TimeSpan.FromDays(request.RememberMe ? 30 : 1);
        return await IssueTokensAsync(user, Guid.CreateVersion7(), lifetime, ip, ct);
    }

    public async Task<TokenResponse> RefreshAsync(string refreshToken, string? ip, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var token = await db.RefreshTokens.Include(t => t.User).ThenInclude(u => u.Roles)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct) ?? throw Unauthorized();

        // A used token coming back means it was stolen or replayed: end every session (BR-47).
        if (token.UsedAt is not null)
        {
            await RevokeAllAsync(token.UserId, ct);
            await db.SaveChangesAsync(ct);
            throw Unauthorized();
        }

        if (token.RevokedAt is not null || token.ExpiresAt <= clock.UtcNow || token.User.Status != "Active" || token.User.IsDeleted)
        {
            throw Unauthorized();
        }

        token.UsedAt = clock.UtcNow;
        var lifetime = token.ExpiresAt - token.CreatedAt;
        return await IssueTokensAsync(token.User, token.FamilyId, lifetime, ip, ct, token);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is { RevokedAt: null })
        {
            token.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task VerifyEmailAsync(string token, CancellationToken ct = default)
    {
        var emailToken = await FindUsableTokenAsync(token, VerifyPurpose, ct);
        emailToken.UsedAt = clock.UtcNow;
        emailToken.User.EmailConfirmed = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task ResendVerificationAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new AppException(404, Messages.NotFound);
        if (user.EmailConfirmed)
        {
            return;
        }

        // The link sent at registration is not a resend, so only later links count (FR-1.7).
        var hourAgo = clock.UtcNow.AddHours(-1);
        var firstLinkEnds = user.CreatedAt.AddSeconds(5);
        var resends = await db.EmailTokens.CountAsync(
            t => t.UserId == userId && t.Purpose == VerifyPurpose && t.CreatedAt > hourAgo && t.CreatedAt > firstLinkEnds, ct);
        if (resends >= MaxResendsPerHour)
        {
            throw new AppException(429, Messages.TooManyLinks);
        }

        QueueVerificationEmail(user);
        await db.SaveChangesAsync(ct);
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken ct = default)
    {
        var normalized = Normalize(email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized && !u.IsDeleted && u.Status == "Active", ct);
        if (user is null)
        {
            return; // The caller always shows MSG10, so nobody learns which emails exist.
        }

        var minutes = await settings.GetIntAsync("resetLinkMinutes", 30, ct);
        var raw = AddEmailToken(user.Id, ResetPurpose, TimeSpan.FromMinutes(minutes));
        QueueEmail(user.Email, "Reset your EMarket password", $"Use this link within {minutes} minutes: /reset-password?token={raw}");
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        EnsureStrong(request.NewPassword);
        var emailToken = await FindUsableTokenAsync(request.Token, ResetPurpose, ct);
        emailToken.UsedAt = clock.UtcNow;
        SetPassword(emailToken.User, request.NewPassword);
        await RevokeAllAsync(emailToken.UserId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TokenResponse> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, string? ip, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new AppException(404, Messages.NotFound);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.OldPassword) == PasswordVerificationResult.Failed)
        {
            throw new AppException(400, Messages.WrongOldPassword);
        }

        if (request.NewPassword == request.OldPassword)
        {
            throw new AppException(400, Messages.Invalid);
        }

        EnsureStrong(request.NewPassword);
        SetPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        await RevokeAllAsync(userId, ct);
        db.AuditLogs.Add(new AuditLog { UserId = userId, Action = "PasswordChanged", EntityName = "User", EntityId = userId, IpAddress = ip });
        return await IssueTokensAsync(user, Guid.CreateVersion7(), TimeSpan.FromDays(1), ip, ct);
    }

    public async Task<bool> IsSessionValidAsync(Guid userId, string securityStamp, CancellationToken ct = default)
    {
        // Cached for at most one minute, so a block or password change bites within 1 minute (NFR-33).
        var current = await cache.GetOrCreateAsync($"session:{userId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return db.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.Status == "Active" && !u.IsDeleted)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync(ct);
        });
        return current == securityStamp;
    }

    private async Task RecordFailedLoginAsync(User user, int lockMinutes, CancellationToken ct)
    {
        var limit = await settings.GetIntAsync("failedLoginLimit", 5, ct);
        user.FailedLoginCount++;
        if (user.FailedLoginCount >= limit)
        {
            user.LockoutEnd = clock.UtcNow.AddMinutes(lockMinutes);
            user.FailedLoginCount = 0;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<TokenResponse> IssueTokensAsync(
        User user, Guid familyId, TimeSpan lifetime, string? ip, CancellationToken ct, RefreshToken? replaces = null)
    {
        var raw = NewRawToken();
        var now = clock.UtcNow;
        var refresh = new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            TokenHash = Hash(raw),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
            CreatedByIp = ip,
        };
        db.RefreshTokens.Add(refresh);
        if (replaces is not null)
        {
            replaces.ReplacedByTokenId = refresh.Id;
        }

        await db.SaveChangesAsync(ct);
        var roles = user.Roles.Select(r => r.Name!).ToList();
        var access = issuer.Issue(user, roles);
        return new TokenResponse(access.Value, access.ExpiresAt, raw, refresh.ExpiresAt, user.Id, user.FullName, user.EmailConfirmed, user.MustChangePassword, roles);
    }

    private async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        active.ForEach(t => t.RevokedAt = clock.UtcNow);
        cache.Remove($"session:{userId}");
    }

    private async Task<EmailToken> FindUsableTokenAsync(string raw, string purpose, CancellationToken ct)
    {
        var hash = Hash(raw);
        var token = await db.EmailTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, ct);
        return token is { UsedAt: null } && token.ExpiresAt > clock.UtcNow ? token : throw new AppException(400, Messages.BadLink);
    }

    private void SetPassword(User user, string password)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
        user.SecurityStamp = NewStamp();
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        cache.Remove($"session:{user.Id}");
    }

    private void QueueVerificationEmail(User user)
    {
        var raw = AddEmailToken(user.Id, VerifyPurpose, TimeSpan.FromHours(24));
        QueueEmail(user.Email, "Verify your EMarket email", $"Open this link within 24 hours: /verify-email?token={raw}");
    }

    private string AddEmailToken(Guid userId, string purpose, TimeSpan lifetime)
    {
        var raw = NewRawToken();
        var now = clock.UtcNow;
        db.EmailTokens.Add(new EmailToken
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        });
        return raw;
    }

    // Saved with the business change and sent later by the outbox job, so mail can never block a request (AD-9).
    private void QueueEmail(string to, string subject, string body) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = "Email",
            Payload = JsonSerializer.Serialize(new { to, subject, body }),
            Status = "Pending",
            NextAttemptAt = clock.UtcNow,
        });

    private static void EnsureStrong(string password)
    {
        if (!PasswordPolicy.IsValid(password))
        {
            throw new AppException(400, Messages.Invalid);
        }
    }

    private static AppException Unauthorized() => new(401, Messages.NotAllowed);

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string NewStamp() => Guid.NewGuid().ToString("N");

    private static string NewRawToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); // 256 bits (NFR-22)

    private static string Hash(string raw) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
