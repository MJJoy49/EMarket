using System.Security.Cryptography;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Identity;

/// <summary>Creates the first admin (SRS 6.7). The random password is returned once and never stored.</summary>
public sealed class AdminSeeder(AppDbContext db, IPasswordHasher<User> hasher)
{
    public async Task<string?> SeedAsync(string email, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(u => u.Roles.Any(r => r.Name == Roles.Admin), ct))
        {
            return null;
        }

        var role = await db.AspNetRoles.FirstAsync(r => r.Name == Roles.Admin, ct);
        var password = $"Em{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}a1";
        var admin = new User
        {
            Id = Guid.CreateVersion7(),
            FullName = "EMarket Admin",
            Email = email.Trim().ToLowerInvariant(),
            EmailConfirmed = true,
            Phone = "01700000000",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Status = "Active",
            MustChangePassword = true,
            PasswordHash = "",
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        admin.Roles.Add(role);
        db.Users.Add(admin);
        await db.SaveChangesAsync(ct);
        return password;
    }
}
