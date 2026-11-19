using ClosedXML.Excel;
using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.BLL.Orders;
using EMarket.DAL.Data;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Admin;
using EMarket.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EMarket.BLL.Admin;

/// <summary>FR-20 and FR-24.9: read-only figures. Reports are cached for 5 minutes so they never slow checkout (NFR-53).</summary>
public sealed class ReportService(AppDbContext db, IMemoryCache cache, IClock clock)
{
    private static readonly TimeSpan BangladeshOffset = TimeSpan.FromHours(6);

    public async Task<AdminDashboardDto> DashboardAsync(CancellationToken ct = default)
    {
        var today = LocalMidnightUtc(clock.UtcNow);
        var placed = db.Orders.AsNoTracking().Where(o => o.Status != OrderRules.AwaitingPayment && o.Status != OrderRules.Cancelled);
        var chart = await SeriesAsync(placed, today.AddDays(-29), today.AddDays(1), "day", ct);
        var returnCutoff = clock.UtcNow.AddDays(-3);
        return new AdminDashboardDto(
            await placed.CountAsync(o => o.CreatedAt >= today, ct),
            await placed.Where(o => o.CreatedAt >= today).SumAsync(o => (decimal?)o.ItemsTotal, ct) ?? 0,
            await db.Users.CountAsync(u => u.CreatedAt >= today, ct),
            await db.Users.CountAsync(ct),
            await db.Shops.CountAsync(ct),
            await db.SellerApplications.CountAsync(a => a.Status == "Pending", ct),
            await db.Products.CountAsync(p => p.Status == "Pending", ct),
            await db.ReturnRequests.CountAsync(r => r.Status == "Requested" && (r.EscalatedAt != null || r.CreatedAt <= returnCutoff), ct),
            await db.Payouts.CountAsync(p => p.Status == "Requested", ct),
            chart);
    }

    public Task<SalesReportDto> SalesAsync(DateOnly? from, DateOnly? to, string groupBy, CancellationToken ct = default)
    {
        var (start, end, f, t) = Range(from, to);
        groupBy = groupBy is "week" or "month" ? groupBy : "day";
        return Cached($"report:sales:{f}:{t}:{groupBy}", async () =>
        {
            var orders = db.Orders.AsNoTracking().Where(o => o.CreatedAt >= start && o.CreatedAt < end && o.Status != OrderRules.AwaitingPayment);
            var live = orders.Where(o => o.Status != OrderRules.Cancelled);
            var delivered = orders.Where(o => o.Status == OrderRules.Delivered);
            // Sorted on a plain projection and mapped to the DTO afterwards, which every provider can translate.
            var products = await db.OrderItems.AsNoTracking().Where(i => i.Order.Status == OrderRules.Delivered && i.Order.CreatedAt >= start && i.Order.CreatedAt < end)
                .GroupBy(i => i.ProductName)
                .Select(g => new { Name = g.Key, Value = g.Sum(i => i.UnitPrice * (i.Quantity - i.ReturnedQuantity)), Count = g.Sum(i => i.Quantity - i.ReturnedQuantity) })
                .OrderByDescending(p => p.Value).Take(10).ToListAsync(ct);
            var sellers = await delivered.GroupBy(o => o.Shop.Name)
                .Select(g => new { Name = g.Key, Value = g.Sum(o => o.ItemsTotal), Count = g.Count() })
                .OrderByDescending(p => p.Value).Take(10).ToListAsync(ct);
            var topProducts = products.Select(p => new ReportPointDto(p.Name, p.Value, p.Count)).ToList();
            var topSellers = sellers.Select(p => new ReportPointDto(p.Name, p.Value, p.Count)).ToList();
            return new SalesReportDto(f, t, groupBy, await live.CountAsync(ct), await live.SumAsync(o => (decimal?)o.ItemsTotal, ct) ?? 0,
                await delivered.SumAsync(o => (decimal?)o.CommissionAmount, ct) ?? 0, await delivered.CountAsync(ct),
                await orders.CountAsync(o => o.Status == OrderRules.Cancelled, ct), await SeriesAsync(live, start, end, groupBy, ct), topProducts, topSellers);
        });
    }

    public Task<UserReportDto> UsersAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var (start, end, f, t) = Range(from, to);
        return Cached($"report:users:{f}:{t}", async () =>
        {
            var created = await db.Users.AsNoTracking().Where(u => u.CreatedAt >= start && u.CreatedAt < end).Select(u => u.CreatedAt).ToListAsync(ct);
            var series = created.GroupBy(d => DateOnly.FromDateTime(d.Add(BangladeshOffset))).OrderBy(g => g.Key)
                .Select(g => new ReportPointDto(g.Key.ToString("dd/MM/yyyy"), g.Count(), g.Count())).ToList();
            return new UserReportDto(created.Count, await db.Users.CountAsync(u => u.Roles.Any(r => r.Name == Roles.Buyer), ct),
                await db.Users.CountAsync(u => u.Roles.Any(r => r.Name == Roles.Seller), ct), series);
        });
    }

    /// <summary>FR-20.5: everything from the ledger. Received - refunds - payouts - seller balances - store credit = platform balance.</summary>
    public Task<FinanceReportDto> FinanceAsync(CancellationToken ct = default) => Cached("report:finance", async () =>
    {
        var byKind = await db.LedgerEntries.AsNoTracking().GroupBy(e => new { e.Account, e.Kind })
            .Select(g => new { g.Key.Account, g.Key.Kind, Total = g.Sum(e => e.Amount) }).ToListAsync(ct);
        decimal Sum(Func<string, string, bool> match) => byKind.Where(x => match(x.Account, x.Kind)).Sum(x => x.Total);
        var received = -Sum((a, k) => a == Ledger.PlatformCash && k == "OrderReceived");
        var refunds = Sum((a, k) => a == Ledger.PlatformCash && k == "RefundPaidOut");
        var payouts = Sum((a, _) => a == Ledger.SellerPaidOut);
        var sellerBalances = Sum((a, _) => a is Ledger.SellerPending or Ledger.SellerAvailable or Ledger.SellerReserved);
        var storeCredit = Sum((a, _) => a == Ledger.StoreCredit);
        var commission = Sum((a, _) => a == Ledger.Commission);
        // Money taken for orders that are not delivered yet is still the buyers', so it is not the platform's (FR-24.2).
        var orderMoneyHeld = Sum((a, _) => a == Ledger.Receivable);
        return new FinanceReportDto(received, refunds, commission, payouts, sellerBalances, storeCredit, orderMoneyHeld,
            received - refunds - payouts - sellerBalances - storeCredit - orderMoneyHeld);
    });

    public async Task<PagedResult<PaymentRowDto>> PaymentsAsync(string? method, string? status, DateOnly? from, DateOnly? to, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.Payments.AsNoTracking().AsQueryable();
        query = string.IsNullOrWhiteSpace(method) ? query : query.Where(p => p.Method == method);
        query = string.IsNullOrWhiteSpace(status) ? query : query.Where(p => p.Status == status);
        if (from is not null || to is not null)
        {
            var (start, end, _, _) = Range(from, to);
            query = query.Where(p => p.CreatedAt >= start && p.CreatedAt < end);
        }

        var total = await query.CountAsync(ct);
        var stale = clock.UtcNow.AddMinutes(-15);
        var rows = await query.OrderByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                p.Id, p.TransactionId, p.Method, p.Amount, p.Status, p.GatewayStatus, p.CreatedAt, p.PaidAt, p.LastCheckedAt,
                Orders = p.CheckoutGroup.Orders.Select(o => o.OrderNumber).ToList(),
            }).ToListAsync(ct);
        // FR-18.10: flag money the gateway took that we have not booked, and attempts nobody has checked for a while.
        var items = rows.Select(p => new PaymentRowDto(p.Id, p.TransactionId, p.Method, p.Amount, p.Status, p.GatewayStatus, p.CreatedAt, p.PaidAt,
            p.LastCheckedAt, string.Join(", ", p.Orders),
            (p.Status == "Initiated" && p.GatewayStatus is "VALID" or "VALIDATED") || (p.Status == "Initiated" && p.CreatedAt < stale))).ToList();
        return new PagedResult<PaymentRowDto>(items, page, pageSize, total);
    }

    public async Task<PagedResult<LedgerRowDto>> LedgerAsync(Guid? orderId, Guid? shopId, Guid? userId, string? kind, DateOnly? from, DateOnly? to, int page, CancellationToken ct = default)
    {
        const int pageSize = 50;
        page = Math.Max(1, page);
        var query = db.LedgerEntries.AsNoTracking().AsQueryable();
        query = orderId is null ? query : query.Where(e => e.OrderId == orderId);
        query = shopId is null ? query : query.Where(e => e.ShopId == shopId);
        query = userId is null ? query : query.Where(e => e.UserId == userId);
        query = string.IsNullOrWhiteSpace(kind) ? query : query.Where(e => e.Kind == kind);
        if (from is not null || to is not null)
        {
            var (start, end, _, _) = Range(from, to);
            query = query.Where(e => e.CreatedAt >= start && e.CreatedAt < end);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.CreatedAt).ThenBy(e => e.TransactionId).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new LedgerRowDto(e.CreatedAt, e.TransactionId, e.Account, e.Kind, e.Amount, e.Order != null ? e.Order.OrderNumber : null,
                e.Shop != null ? e.Shop.Name : null, e.User != null ? e.User.FullName : null, e.Note))
            .ToListAsync(ct);
        return new PagedResult<LedgerRowDto>(items, page, pageSize, total);
    }

    /// <summary>FR-24.8: transactions whose lines do not add up to 0 (there should never be any).</summary>
    public async Task<IReadOnlyList<Guid>> UnbalancedTransactionsAsync(CancellationToken ct = default) =>
        await db.LedgerEntries.GroupBy(e => e.TransactionId).Where(g => g.Sum(e => e.Amount) != 0).Select(g => g.Key).Take(100).ToListAsync(ct);

    /// <summary>FR-20.6: the Excel file holds the same rows and totals as the screen.</summary>
    public static byte[] ToExcel(string sheet, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var book = new XLWorkbook();
        var ws = book.Worksheets.Add(sheet);
        for (var c = 0; c < headers.Count; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
            {
                ws.Cell(r, c + 1).Value = row[c] switch
                {
                    null => Blank.Value,
                    decimal d => d,
                    int i => i,
                    DateTime dt => dt.Add(BangladeshOffset),
                    DateOnly day => day.ToDateTime(TimeOnly.MinValue),
                    var other => other.ToString(),
                };
            }

            r++;
        }

        ws.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<IReadOnlyList<ReportPointDto>> SeriesAsync(IQueryable<Domain.Entities.Order> orders, DateTime start, DateTime end, string groupBy, CancellationToken ct)
    {
        var rows = await orders.Where(o => o.CreatedAt >= start && o.CreatedAt < end).Select(o => new { o.CreatedAt, o.ItemsTotal }).ToListAsync(ct);
        string Key(DateTime utc)
        {
            var local = utc.Add(BangladeshOffset);
            return groupBy switch
            {
                "month" => local.ToString("MM/yyyy"),
                "week" => $"Week of {local.AddDays(-(((int)local.DayOfWeek + 1) % 7)):dd/MM/yyyy}", // weeks start on Saturday in Bangladesh
                _ => local.ToString("dd/MM/yyyy"),
            };
        }

        var grouped = rows.GroupBy(o => Key(o.CreatedAt)).ToDictionary(g => g.Key, g => (Sum: g.Sum(o => o.ItemsTotal), Count: g.Count()));
        var labels = new List<string>();
        for (var d = start; d < end; d = d.AddDays(1))
        {
            var key = Key(d);
            if (!labels.Contains(key))
            {
                labels.Add(key);
            }
        }

        return labels.Select(l => new ReportPointDto(l, grouped.GetValueOrDefault(l).Sum, grouped.GetValueOrDefault(l).Count)).ToList();
    }

    /// <summary>FR-20.8: the range defaults to the last 30 days and cannot be longer than a year.</summary>
    private (DateTime Start, DateTime End, DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.Add(BangladeshOffset));
        var t = to ?? today;
        var f = from ?? t.AddDays(-29);
        if (f > t || f < t.AddYears(-1))
        {
            throw new AppException(400, Messages.Invalid);
        }

        var start = f.ToDateTime(TimeOnly.MinValue) - BangladeshOffset;
        var end = t.AddDays(1).ToDateTime(TimeOnly.MinValue) - BangladeshOffset;
        return (start, end, f, t);
    }

    private DateTime LocalMidnightUtc(DateTime utc) => utc.Add(BangladeshOffset).Date - BangladeshOffset;

    private async Task<T> Cached<T>(string key, Func<Task<T>> load) =>
        (await cache.GetOrCreateAsync(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return load();
        }))!;
}
