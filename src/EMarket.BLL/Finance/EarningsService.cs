using EMarket.BLL.Common;
using EMarket.BLL.Orders;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Sellers;
using EMarket.Shared.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Finance;

/// <summary>FR-15 and FR-18.6 to 18.9: seller balances (always read from the ledger), payouts, and shop settings.</summary>
public sealed class EarningsService(
    AppDbContext db, Ledger ledger, Notifier notifier, ISettingsReader settings, ISecretProtector secrets,
    IPasswordHasher<User> hasher, IFileStorage files, IClock clock)
{
    private static readonly string[] OpenReturn = ["Requested", "Approved", "ItemReceived"];

    public async Task<BalancesDto> BalancesAsync(Guid shopId, CancellationToken ct = default)
    {
        var b = await ledger.ShopBalancesAsync(shopId, ct);
        decimal Get(string account) => b.TryGetValue(account, out var v) ? v : 0;
        return new BalancesDto(Get(Ledger.SellerPending), Get(Ledger.SellerAvailable), Get(Ledger.SellerReserved), Get(Ledger.SellerPaidOut));
    }

    public async Task<SellerDashboardDto> DashboardAsync(Guid sellerId, CancellationToken ct = default)
    {
        var shop = await OwnShopAsync(sellerId, ct);
        var today = clock.UtcNow.AddHours(6).Date.AddHours(-6); // midnight in Bangladesh (UTC+6), in UTC (NFR-12)
        var orders = db.Orders.AsNoTracking().Where(o => o.ShopId == shop.Id && o.Status != OrderRules.AwaitingPayment && o.Status != OrderRules.Cancelled);
        var last30 = await orders.Where(o => o.CreatedAt >= today.AddDays(-29))
            .Select(o => new { o.CreatedAt, o.ItemsTotal }).ToListAsync(ct);
        var chart = Enumerable.Range(0, 30).Select(i => today.AddDays(-29 + i))
            .Select(day => new DailySalesDto(DateOnly.FromDateTime(day.AddHours(6)), last30.Where(o => o.CreatedAt >= day && o.CreatedAt < day.AddDays(1)).Sum(o => o.ItemsTotal)))
            .ToList();
        var lowLimit = await settings.GetIntAsync("lowStockLimit", 5, ct);
        var lowStock = await db.ProductVariants.CountAsync(v => v.Product.ShopId == shop.Id && v.IsActive && v.Stock <= lowLimit && v.Product.Status == "Active", ct)
            + await db.Products.CountAsync(p => p.ShopId == shop.Id && p.Status == "Active" && !p.ProductVariants.Any(v => v.IsActive) && p.Stock <= lowLimit, ct);
        return new SellerDashboardDto(
            await orders.CountAsync(o => o.Status == OrderRules.Pending, ct),
            await orders.CountAsync(o => o.Status == OrderRules.Confirmed || o.Status == OrderRules.Processing, ct),
            chart[^1].Sales, chart.TakeLast(7).Sum(d => d.Sales), chart.Sum(d => d.Sales),
            await BalancesAsync(shop.Id, ct), lowStock,
            await db.ReturnRequests.CountAsync(r => r.Order.ShopId == shop.Id && r.Status == "Requested" && r.EscalatedAt == null, ct),
            await db.Products.CountAsync(p => p.ShopId == shop.Id, ct), chart);
    }

    public async Task<IReadOnlyList<EarningRowDto>> EarningsAsync(Guid sellerId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var shop = await OwnShopAsync(sellerId, ct);
        var query = db.Orders.AsNoTracking().Where(o => o.ShopId == shop.Id && o.Status == OrderRules.Delivered);
        query = from is null ? query : query.Where(o => o.DeliveredAt >= from);
        query = to is null ? query : query.Where(o => o.DeliveredAt < to.Value.AddDays(1));
        var rows = await query.OrderByDescending(o => o.DeliveredAt).Take(1000)
            .Select(o => new
            {
                o.DeliveredAt, o.Id, o.OrderNumber, o.ItemsTotal, o.CommissionAmount, o.DeliveryFee,
                Released = o.LedgerEntries.Any(e => e.Kind == "EarningReleased"),
                Reversed = o.LedgerEntries.Where(e => e.Kind == "RefundReversal" && e.ShopId != null).Sum(e => (decimal?)e.Amount) ?? 0,
            }).ToListAsync(ct);
        return rows.Select(r => new EarningRowDto(r.DeliveredAt!.Value, r.Id, r.OrderNumber, r.ItemsTotal, r.CommissionAmount, r.DeliveryFee,
            r.ItemsTotal - r.CommissionAmount + r.DeliveryFee + r.Reversed, r.Released ? "Available" : "Pending")).ToList();
    }

    /// <summary>FR-15.4: 7 days after delivery, with no open return, the earning becomes available.</summary>
    public async Task<int> ReleaseDueAsync(CancellationToken ct = default)
    {
        var days = await settings.GetIntAsync("returnWindowDays", 7, ct);
        var cutoff = clock.UtcNow.AddDays(-days);
        // Picks orders that still hold a pending earning, so a fully refunded order is never picked again.
        var due = await db.Orders.Where(o => o.Status == OrderRules.Delivered && o.DeliveredAt <= cutoff
                && o.LedgerEntries.Where(e => e.Account == Ledger.SellerPending).Sum(e => e.Amount) > 0
                && !o.ReturnRequests.Any(r => OpenReturn.Contains(r.Status)))
            .Select(o => new { o.Id, o.ShopId }).Take(200).ToListAsync(ct);
        foreach (var order in due)
        {
            var pending = await db.LedgerEntries.Where(e => e.OrderId == order.Id && e.Account == Ledger.SellerPending).SumAsync(e => e.Amount, ct);
            await ledger.PostAsync(Ledger.EventId($"release:{order.Id}"), [
                new(Ledger.SellerPending, -pending, "EarningReleased", ShopId: order.ShopId, OrderId: order.Id, RefId: order.Id),
                new(Ledger.SellerAvailable, pending, "EarningReleased", ShopId: order.ShopId, OrderId: order.Id, RefId: order.Id),
            ], ct);
        }

        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    // ---------- payouts ----------

    public async Task<Guid> RequestPayoutAsync(Guid sellerId, decimal amount, CancellationToken ct = default)
    {
        var shop = await OwnShopAsync(sellerId, ct);
        var balances = await BalancesAsync(shop.Id, ct);
        var minimum = await settings.GetDecimalAsync("minPayout", 500, ct);
        if (await db.Payouts.AnyAsync(p => p.ShopId == shop.Id && p.Status == "Requested", ct))
        {
            throw new AppException(409, Messages.Conflict); // BR-24: one open request
        }

        if (amount < minimum || amount > balances.Available)
        {
            throw new AppException(400, amount < minimum ? Messages.Invalid : Messages.OverBalance); // MSG25; also blocks a negative balance (BR-25)
        }

        var payout = new Payout
        {
            Id = Guid.CreateVersion7(), ShopId = shop.Id, Amount = amount, Method = shop.PayoutMethod, AccountSnapshot = shop.PayoutAccount, Status = "Requested",
        };
        db.Payouts.Add(payout);
        await ledger.PostAsync(Ledger.EventId($"payout-request:{payout.Id}"), [
            new(Ledger.SellerAvailable, -amount, "PayoutRequested", ShopId: shop.Id, RefId: payout.Id),
            new(Ledger.SellerReserved, amount, "PayoutRequested", ShopId: shop.Id, RefId: payout.Id),
        ], ct);
        await notifier.NotifyAdminsAsync("PayoutRequested", "Payout requested", $"{shop.Name} asks for Tk {amount:N2}.", "/admin/payouts", ct);
        await db.SaveChangesAsync(ct);
        return payout.Id;
    }

    public async Task CancelPayoutAsync(Guid sellerId, Guid payoutId, CancellationToken ct = default)
    {
        var shop = await OwnShopAsync(sellerId, ct);
        var payout = await db.Payouts.FirstOrDefaultAsync(p => p.Id == payoutId && p.ShopId == shop.Id, ct) ?? throw new AppException(404, Messages.NotFound);
        await CloseAsync(payout, "Cancelled", null, null, null, ct);
    }

    public async Task<PagedResult<PayoutDto>> PayoutsAsync(Guid? sellerId, string? status, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.Payouts.AsNoTracking().Include(p => p.Shop).AsQueryable();
        if (sellerId is not null)
        {
            var shop = await OwnShopAsync(sellerId.Value, ct);
            query = query.Where(p => p.ShopId == shop.Id);
        }

        query = string.IsNullOrWhiteSpace(status) ? query : query.Where(p => p.Status == status);
        var total = await query.CountAsync(ct);
        // FR-18.6: the admin works oldest first; the seller sees newest first.
        query = sellerId is null ? query.OrderBy(p => p.CreatedAt) : query.OrderByDescending(p => p.CreatedAt);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var result = new List<PayoutDto>();
        foreach (var p in rows)
        {
            var forAdmin = sellerId is null;
            result.Add(new PayoutDto(p.Id, p.ShopId, p.Shop.Name, p.Amount, p.Method, p.Status, p.ReferenceNo, p.RejectionReason, p.CreatedAt, p.DecidedAt,
                forAdmin ? secrets.Unprotect(p.AccountSnapshot) : null, forAdmin && p.Status == "Requested" ? await BalancesAsync(p.ShopId, ct) : null));
        }

        return new PagedResult<PayoutDto>(result, page, pageSize, total);
    }

    /// <summary>FR-18.7: the money is sent outside the system first; then the admin records it with a reference.</summary>
    public async Task PayAsync(Guid adminId, Guid payoutId, PayPayoutRequest request, CancellationToken ct = default)
    {
        if (request.PaidOn > DateOnly.FromDateTime(clock.UtcNow.AddHours(6)))
        {
            throw new AppException(400, Messages.Invalid);
        }

        var payout = await db.Payouts.Include(p => p.Shop).ThenInclude(s => s.Owner).FirstOrDefaultAsync(p => p.Id == payoutId, ct) ?? throw new AppException(404, Messages.NotFound);
        await CloseAsync(payout, "Paid", adminId, request.ReferenceNo.Trim(), null, ct);
    }

    public async Task RejectPayoutAsync(Guid adminId, Guid payoutId, string reason, CancellationToken ct = default)
    {
        var payout = await db.Payouts.Include(p => p.Shop).ThenInclude(s => s.Owner).FirstOrDefaultAsync(p => p.Id == payoutId, ct) ?? throw new AppException(404, Messages.NotFound);
        await CloseAsync(payout, "Rejected", adminId, null, reason.Trim(), ct);
    }

    private async Task CloseAsync(Payout payout, string status, Guid? adminId, string? reference, string? reason, CancellationToken ct)
    {
        if (payout.Status != "Requested")
        {
            throw new AppException(409, Messages.Conflict);
        }

        payout.Status = status;
        payout.ReferenceNo = reference;
        payout.RejectionReason = reason;
        payout.DecidedById = adminId;
        payout.DecidedAt = clock.UtcNow;
        var target = status == "Paid" ? Ledger.SellerPaidOut : Ledger.SellerAvailable; // FR-24.4
        await ledger.PostAsync(Ledger.EventId($"payout-{status.ToLowerInvariant()}:{payout.Id}"), [
            new(Ledger.SellerReserved, -payout.Amount, $"Payout{status}", ShopId: payout.ShopId, RefId: payout.Id),
            new(target, payout.Amount, $"Payout{status}", ShopId: payout.ShopId, RefId: payout.Id),
        ], ct);
        if (adminId is not null)
        {
            notifier.Audit(adminId, $"Payout{status}", nameof(Payout), payout.Id, newValue: reference ?? reason);
            notifier.Notify(payout.Shop.OwnerId, $"Payout{status}", $"Payout {status.ToLowerInvariant()}",
                status == "Paid" ? $"Tk {payout.Amount:N2} was sent. Reference: {reference}." : $"Your payout request was rejected: {reason}", "/seller/earnings");
            notifier.Email(payout.Shop.Owner.Email, $"Your EMarket payout was {status.ToLowerInvariant()}",
                status == "Paid" ? $"Tk {payout.Amount:N2} was sent. Reference: {reference}." : $"Reason: {reason}. The amount is available again.");
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AppException(409, Messages.Conflict);
        }
    }

    // ---------- shop settings ----------

    public async Task<ShopSettingsDto> ShopAsync(Guid sellerId, CancellationToken ct = default)
    {
        var shop = await OwnShopAsync(sellerId, ct);
        var account = secrets.Unprotect(shop.PayoutAccount);
        var masked = account.Length <= 4 ? "****" : new string('*', Math.Min(8, account.Length - 4)) + account[^4..];
        return new ShopSettingsDto(shop.Id, shop.Name, shop.Slug, shop.Description, shop.LogoUrl, shop.BannerUrl, shop.Status,
            shop.InsideDhakaFee ?? 60, shop.OutsideDhakaFee ?? 60, shop.FreeDeliveryAbove ?? 2000, shop.PayoutMethod, masked);
    }

    public async Task UpdateShopAsync(Guid sellerId, ShopSettingsRequest request, CancellationToken ct = default)
    {
        var shop = await OwnShopAsync(sellerId, ct);
        var name = request.Name.Trim();
        if (await db.Shops.IgnoreQueryFilters().AnyAsync(s => s.Id != shop.Id && s.Name.ToLower() == name.ToLower(), ct))
        {
            throw new AppException(409, Messages.Conflict);
        }

        if (shop.Status == "Suspended")
        {
            throw new AppException(403, Messages.ShopUnavailable);
        }

        shop.Name = name;
        shop.Description = request.Description?.Trim() ?? "";
        shop.InsideDhakaFee = request.InsideDhakaFee;
        shop.OutsideDhakaFee = request.OutsideDhakaFee;
        shop.FreeDeliveryAbove = request.FreeDeliveryAbove;
        shop.Status = request.Vacation ? "Vacation" : "Active";
        await db.SaveChangesAsync(ct);
    }

    public async Task<string> SetShopImageAsync(Guid sellerId, string kind, UploadFile file, CancellationToken ct = default)
    {
        var shop = await OwnShopAsync(sellerId, ct);
        var path = await files.SaveImageAsync(file, "shops", ct);
        if (kind == "logo")
        {
            shop.LogoUrl = path;
        }
        else
        {
            shop.BannerUrl = path;
        }

        await db.SaveChangesAsync(ct);
        return path;
    }

    /// <summary>FR-15.10: needs the password again, alerts the seller by email and only affects later payout requests.</summary>
    public async Task UpdatePayoutDetailsAsync(Guid sellerId, PayoutDetailsRequest request, CancellationToken ct = default)
    {
        var user = await db.Users.FirstAsync(u => u.Id == sellerId, ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            throw new AppException(400, Messages.WrongOldPassword);
        }

        string account;
        if (request.PayoutMethod == "BankTransfer")
        {
            string?[] needed = [request.BankName, request.BankBranch, request.RoutingNumber, request.AccountName, request.AccountNumber];
            account = needed.Any(string.IsNullOrWhiteSpace)
                ? throw new AppException(400, Messages.Required)
                : $"{request.BankName}|{request.BankBranch}|{request.RoutingNumber}|{request.AccountName}|{request.AccountNumber}";
        }
        else
        {
            account = request.WalletNumber ?? throw new AppException(400, Messages.Required);
        }

        var shop = await OwnShopAsync(sellerId, ct);
        shop.PayoutMethod = request.PayoutMethod;
        shop.PayoutAccount = secrets.Protect(account);
        notifier.Audit(sellerId, "PayoutDetailsChanged", nameof(Shop), shop.Id, newValue: request.PayoutMethod);
        notifier.Notify(sellerId, "PayoutDetailsChanged", "Payout details changed", "Your payout details were changed. Contact support if this was not you.");
        notifier.Email(user.Email, "Your EMarket payout details were changed", "Your payout details were changed. If this was not you, contact support now.");
        await db.SaveChangesAsync(ct);
    }

    private async Task<Shop> OwnShopAsync(Guid sellerId, CancellationToken ct) =>
        await db.Shops.FirstOrDefaultAsync(s => s.OwnerId == sellerId, ct) ?? throw new AppException(403, Messages.NotAllowed);
}
