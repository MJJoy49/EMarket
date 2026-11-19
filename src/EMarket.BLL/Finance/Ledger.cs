using System.Security.Cryptography;
using System.Text;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Finance;

/// <summary>
/// The append-only money ledger (FR-24). Every event writes lines that add up to 0.
/// Sign rule: money the platform owes or has earned is positive (store credit, seller balances, commission,
/// money held for an order); PlatformCash is the other side, so cash coming in is negative and cash going out is positive.
/// </summary>
public sealed class Ledger(AppDbContext db)
{
    public const string PlatformCash = "PlatformCash";
    public const string Receivable = "Receivable";
    public const string StoreCredit = "StoreCredit";
    public const string SellerPending = "SellerPending";
    public const string SellerAvailable = "SellerAvailable";
    public const string SellerReserved = "SellerReserved";
    public const string SellerPaidOut = "SellerPaidOut";
    public const string Commission = "Commission";
    public const string Refunds = "Refunds";

    public sealed record Line(string Account, decimal Amount, string Kind, Guid? ShopId = null, Guid? UserId = null, Guid? OrderId = null, Guid? RefId = null, string? Note = null);

    /// <summary>One id per business event, so handling the same event twice books nothing new (FR-24.10).</summary>
    public static Guid EventId(string eventName) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(eventName)).AsSpan(0, 16));

    /// <summary>Adds the lines to the current unit of work. Returns false when this event was already booked.</summary>
    public async Task<bool> PostAsync(Guid transactionId, IEnumerable<Line> lines, CancellationToken ct = default)
    {
        var real = lines.Where(l => l.Amount != 0).ToList();
        if (real.Count == 0 || db.LedgerEntries.Local.Any(e => e.TransactionId == transactionId)
            || await db.LedgerEntries.AnyAsync(e => e.TransactionId == transactionId, ct))
        {
            return false;
        }

        if (real.Sum(l => l.Amount) != 0)
        {
            throw new InvalidOperationException("Ledger transaction does not balance."); // FR-24.5, a programming error
        }

        foreach (var line in real)
        {
            db.LedgerEntries.Add(new LedgerEntry
            {
                TransactionId = transactionId,
                Account = line.Account,
                Amount = line.Amount,
                Kind = line.Kind,
                ShopId = line.ShopId,
                UserId = line.UserId,
                OrderId = line.OrderId,
                RefId = line.RefId,
                Note = line.Note,
            });
        }

        return true;
    }

    public async Task<decimal> StoreCreditAsync(Guid userId, CancellationToken ct = default) =>
        await db.LedgerEntries.Where(e => e.UserId == userId && e.Account == StoreCredit).SumAsync(e => (decimal?)e.Amount, ct) ?? 0;

    public async Task<IReadOnlyDictionary<string, decimal>> ShopBalancesAsync(Guid shopId, CancellationToken ct = default) =>
        await db.LedgerEntries.Where(e => e.ShopId == shopId).GroupBy(e => e.Account)
            .Select(g => new { g.Key, Total = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);

    /// <summary>Splits an amount over weights so the parts add up exactly; the last part takes the rounding rest.</summary>
    public static decimal[] Split(decimal amount, IReadOnlyList<decimal> weights)
    {
        var total = weights.Sum();
        var parts = new decimal[weights.Count];
        if (total == 0 || weights.Count == 0)
        {
            return parts;
        }

        decimal used = 0;
        for (var i = 0; i < weights.Count - 1; i++)
        {
            parts[i] = Math.Round(amount * weights[i] / total, 2, MidpointRounding.AwayFromZero);
            used += parts[i];
        }

        parts[^1] = amount - used;
        return parts;
    }
}
