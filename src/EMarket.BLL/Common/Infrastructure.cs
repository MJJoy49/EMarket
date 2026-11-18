using EMarket.DAL.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EMarket.BLL.Common;

/// <summary>A rule was broken. The API turns this into the status code and the MSG text.</summary>
public sealed class AppException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public interface ISettingsReader
{
    Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default);
    Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct = default);
}

/// <summary>Reads SystemSetting; values are cached for at most one minute (FR-21.7).</summary>
public sealed class SettingsReader(AppDbContext db, IMemoryCache cache) : ISettingsReader
{
    public async Task<int> GetIntAsync(string key, int fallback, CancellationToken ct = default) =>
        int.TryParse(await ReadAsync(key, ct), out var value) ? value : fallback;

    public async Task<decimal> GetDecimalAsync(string key, decimal fallback, CancellationToken ct = default) =>
        decimal.TryParse(await ReadAsync(key, ct), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private async Task<string?> ReadAsync(string key, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"setting:{key}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return db.SystemSettings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        });
}
