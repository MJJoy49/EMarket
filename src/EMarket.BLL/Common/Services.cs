using System.Text.Json;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace EMarket.BLL.Common;

/// <summary>A file sent by a user. The BLL checks it; the API only hands it over.</summary>
public sealed record UploadFile(Stream Content, long Length);

public sealed record SavedImage(string Path, string ThumbPath);

public interface IFileStorage
{
    /// <summary>Checks the real file type (JPG or PNG by signature) and the 2 MB limit, then saves under a random name.</summary>
    Task<string> SaveImageAsync(UploadFile file, string folder, CancellationToken ct = default);

    /// <summary>Same checks, plus a small copy for lists (NFR-56).</summary>
    Task<SavedImage> SaveImageWithThumbnailAsync(UploadFile file, string folder, CancellationToken ct = default);

    Stream? Open(string path);

    void Delete(string path);
}

/// <summary>AD-11: files live in a private folder outside the web root; the folder is a setting (NFR-49).</summary>
public sealed class LocalFileStorage(IConfiguration config) : IFileStorage
{
    public const long MaxBytes = 2 * 1024 * 1024;
    private readonly string root = Path.GetFullPath(config["Storage:Root"] ?? Path.Combine(AppContext.BaseDirectory, "private-files"));

    public async Task<string> SaveImageAsync(UploadFile file, string folder, CancellationToken ct = default) =>
        (await SaveAsync(file, folder, thumbnail: false, ct)).Path;

    public Task<SavedImage> SaveImageWithThumbnailAsync(UploadFile file, string folder, CancellationToken ct = default) =>
        SaveAsync(file, folder, thumbnail: true, ct);

    private async Task<SavedImage> SaveAsync(UploadFile file, string folder, bool thumbnail, CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaxBytes)
        {
            throw new AppException(400, Messages.BadFile);
        }

        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var extension = ImageExtension(bytes);
        if (extension is null || bytes.Length > MaxBytes)
        {
            throw new AppException(400, Messages.BadFile);
        }

        // NFR-37: decode and encode again, which drops hidden data such as EXIF and anything appended to the file.
        using var bitmap = Decode(bytes) ?? throw new AppException(400, Messages.BadFile);
        var name = Guid.NewGuid().ToString("N");
        var format = extension == ".png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        var path = await WriteAsync(bitmap, Path.Combine(folder, name + extension), format, ct);
        if (!thumbnail)
        {
            return new SavedImage(path, path);
        }

        const int side = 320;
        var scale = Math.Min(1f, side / (float)Math.Max(bitmap.Width, bitmap.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale)));
        using var small = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new AppException(400, Messages.BadFile);
        var thumb = await WriteAsync(small, Path.Combine(folder, name + "_thumb.jpg"), SKEncodedImageFormat.Jpeg, ct);
        return new SavedImage(path, thumb);
    }

    // SKBitmap.Decode throws on some broken files instead of returning null, so open the codec first.
    private static SKBitmap? Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        return codec is null ? null : SKBitmap.Decode(codec);
    }

    private async Task<string> WriteAsync(SKBitmap bitmap, string relative, SKEncodedImageFormat format, CancellationToken ct)
    {
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 85) ?? throw new AppException(400, Messages.BadFile);
        await File.WriteAllBytesAsync(full, data.ToArray(), ct);
        return relative.Replace('\\', '/');
    }

    public Stream? Open(string path)
    {
        var full = Resolve(path);
        return File.Exists(full) ? File.OpenRead(full) : null;
    }

    public void Delete(string path)
    {
        var full = Resolve(path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    public static string? ImageExtension(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ".png",
        _ => null,
    };

    // Stored paths are relative; refuse anything that would climb out of the private root.
    private string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full
            : throw new AppException(400, Messages.Invalid);
    }
}

/// <summary>DB-7: NID numbers and payout accounts are encrypted before they reach the database.</summary>
public interface ISecretProtector
{
    string Protect(string value);
    string Unprotect(string value);
}

public sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("EMarket.PersonalData.v1");

    public string Protect(string value) => protector.Protect(value);

    public string Unprotect(string value) => protector.Unprotect(value);
}

/// <summary>Writes in-app notifications, queued emails and audit rows into the current unit of work.</summary>
public sealed class Notifier(AppDbContext db, IClock clock)
{
    public void Notify(Guid userId, string type, string title, string message, string? link = null) =>
        db.Notifications.Add(new Notification { UserId = userId, Type = type, Title = title, Message = message, LinkUrl = link });

    public async Task NotifyAdminsAsync(string type, string title, string message, string? link = null, CancellationToken ct = default)
    {
        var admins = await db.Users.Where(u => u.Status == "Active" && u.Roles.Any(r => r.Name == Roles.Admin)).Select(u => u.Id).ToListAsync(ct);
        admins.ForEach(id => Notify(id, type, title, message, link));
    }

    public void Email(string to, string subject, string body) =>
        db.OutboxMessages.Add(new OutboxMessage
        {
            Type = "Email",
            Payload = JsonSerializer.Serialize(new { to, subject, body }),
            Status = "Pending",
            NextAttemptAt = clock.UtcNow,
        });

    public void Audit(Guid? userId, string action, string entityName, Guid? entityId, string? oldValue = null, string? newValue = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
        });
}
