using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace EMarket.Tests.TestUtils;

/// <summary>An in-memory database with the three roles, plus helpers to add common rows.</summary>
public static class TestDb
{
    private static int orderSequence;

    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new AppDbContext(options);

        // SQL Server fills rowversion columns itself; the in-memory provider does not, so imitate it.
        db.SavingChanges += (_, _) =>
        {
            foreach (var entry in db.ChangeTracker.Entries())
            {
                if (entry.State is EntityState.Added or EntityState.Modified && entry.Metadata.FindProperty("RowVersion") is not null)
                {
                    entry.Property("RowVersion").CurrentValue = Guid.NewGuid().ToByteArray()[..8];
                }

                // SQL Server takes the order number from a sequence (DB-6).
                if (entry is { State: EntityState.Added, Entity: Order { OrderNumber: null } order })
                {
                    order.OrderNumber = $"EM-20261118-{Interlocked.Increment(ref orderSequence):D6}";
                }
            }
        };

        foreach (var role in new[] { Roles.Buyer, Roles.Seller, Roles.Admin })
        {
            db.AspNetRoles.Add(new AspNetRole { Id = Guid.NewGuid(), Name = role, NormalizedName = role.ToUpperInvariant() });
        }

        db.SaveChanges();
        return db;
    }

    public static User AddUser(this AppDbContext db, string role = Roles.Buyer, bool emailConfirmed = true, string? email = null)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            FullName = "Test User",
            Email = email ?? $"{Guid.NewGuid():N}@example.com",
            Phone = "01712345678",
            PasswordHash = "hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            Status = "Active",
            EmailConfirmed = emailConfirmed,
        };
        user.Roles.Add(db.AspNetRoles.Single(r => r.Name == role));
        db.Users.Add(user);
        db.SaveChanges();
        return user;
    }
}

public sealed class FakeClock : IClock
{
    // Starts at the real time, because the DAL stamps CreatedAt and UpdatedAt with the real clock.
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}

public sealed class FakeFiles : IFileStorage
{
    public List<string> Saved { get; } = [];

    public Task<string> SaveImageAsync(UploadFile file, string folder, CancellationToken ct = default)
    {
        var path = $"{folder}/{Guid.NewGuid():N}.jpg";
        Saved.Add(path);
        return Task.FromResult(path);
    }

    public async Task<SavedImage> SaveImageWithThumbnailAsync(UploadFile file, string folder, CancellationToken ct = default)
    {
        var path = await SaveImageAsync(file, folder, ct);
        return new SavedImage(path, path.Replace(".jpg", "_thumb.jpg"));
    }

    public Stream? Open(string path) => Saved.Contains(path) ? new MemoryStream([1, 2, 3]) : null;

    public void Delete(string path) => Saved.Remove(path);
}

public sealed class FakeSecrets : ISecretProtector
{
    public string Protect(string value) => $"enc:{value}";

    public string Unprotect(string value) => value["enc:".Length..];
}
