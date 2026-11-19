using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Orders;
using EMarket.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Orders;

/// <summary>Order tracking for buyers (FR-10), sellers (FR-11) and admins (FR-18), with the effects of each status change.</summary>
public sealed class OrderService(AppDbContext db, Ledger ledger, Notifier notifier, ISettingsReader settings, IClock clock)
{
    private const int PageSize = 20;

    // ---------- reading ----------

    public Task<PagedResult<OrderListItemDto>> BuyerListAsync(Guid buyerId, string? status, int page, CancellationToken ct = default) =>
        ListAsync(db.Orders.Where(o => o.BuyerId == buyerId), status, page, Actor.Buyer, ct);

    /// <summary>FR-11.1: an order still waiting for online payment is not shown to the seller.</summary>
    public async Task<PagedResult<OrderListItemDto>> SellerListAsync(Guid sellerId, string? status, DateTime? from, DateTime? to, int page, CancellationToken ct = default)
    {
        var shopId = await OwnShopIdAsync(sellerId, ct);
        var query = db.Orders.Where(o => o.ShopId == shopId && o.Status != OrderRules.AwaitingPayment);
        query = from is null ? query : query.Where(o => o.CreatedAt >= from);
        query = to is null ? query : query.Where(o => o.CreatedAt < to.Value.AddDays(1));
        return await ListAsync(query, status, page, Actor.Seller, ct);
    }

    public Task<PagedResult<OrderListItemDto>> AdminListAsync(string? status, string? search, Guid? shopId, string? paymentStatus, DateTime? from, DateTime? to, int page, CancellationToken ct = default)
    {
        var query = db.Orders.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.OrderNumber.Contains(term) || o.Buyer.FullName.Contains(term) || o.Buyer.Email.Contains(term) || o.Shop.Name.Contains(term));
        }

        query = shopId is null ? query : query.Where(o => o.ShopId == shopId);
        query = from is null ? query : query.Where(o => o.CreatedAt >= from);
        query = to is null ? query : query.Where(o => o.CreatedAt < to.Value.AddDays(1));
        if (!string.IsNullOrWhiteSpace(paymentStatus))
        {
            query = query.Where(o => o.CheckoutGroup.Payments.OrderByDescending(p => p.CreatedAt).Select(p => p.Status).FirstOrDefault() == paymentStatus);
        }

        return ListAsync(query, status, page, Actor.Admin, ct);
    }

    public async Task<OrderDetailDto> BuyerDetailAsync(Guid buyerId, Guid orderId, CancellationToken ct = default)
    {
        // FR-10.10: another buyer's order answers 404, so its existence is not shown.
        var order = await DetailQuery().FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, ct) ?? throw new AppException(404, Messages.NotFound);
        return await ToDetailAsync(order, Actor.Buyer, ct);
    }

    public async Task<OrderDetailDto> SellerDetailAsync(Guid sellerId, Guid orderId, CancellationToken ct = default)
    {
        var shopId = await OwnShopIdAsync(sellerId, ct);
        var order = await DetailQuery().FirstOrDefaultAsync(o => o.Id == orderId && o.ShopId == shopId && o.Status != OrderRules.AwaitingPayment, ct)
            ?? throw new AppException(404, Messages.NotFound);
        return await ToDetailAsync(order, Actor.Seller, ct);
    }

    public async Task<OrderDetailDto> AdminDetailAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await DetailQuery().FirstOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new AppException(404, Messages.NotFound);
        return await ToDetailAsync(order, Actor.Admin, ct);
    }

    // ---------- buyer actions ----------

    public async Task CancelByBuyerAsync(Guid buyerId, Guid orderId, string reason, CancellationToken ct = default)
    {
        var order = await LoadForChangeAsync(o => o.Id == orderId && o.BuyerId == buyerId, ct);
        if (!OrderRules.CanMove(order.Status, OrderRules.Cancelled, Actor.Buyer))
        {
            throw new AppException(409, Messages.CannotCancel); // BR-14, MSG20
        }

        await ChangeStatusAsync(order, OrderRules.Cancelled, Actor.Buyer, buyerId, reason, ct);
        await SaveAsync(ct);
    }

    // ---------- seller actions ----------

    public async Task SellerMoveAsync(Guid sellerId, Guid orderId, string to, string? note, CancellationToken ct = default)
    {
        var shopId = await OwnShopIdAsync(sellerId, ct);
        var order = await LoadForChangeAsync(o => o.Id == orderId && o.ShopId == shopId && o.Status != OrderRules.AwaitingPayment, ct);
        if (to == OrderRules.Cancelled && string.IsNullOrWhiteSpace(note))
        {
            throw new AppException(400, Messages.Required);
        }

        await ChangeStatusAsync(order, to, Actor.Seller, sellerId, note, ct);
        await SaveAsync(ct);
    }

    public async Task ShipAsync(Guid sellerId, Guid orderId, ShipRequest request, CancellationToken ct = default)
    {
        var shopId = await OwnShopIdAsync(sellerId, ct);
        var order = await LoadForChangeAsync(o => o.Id == orderId && o.ShopId == shopId, ct);
        order.CourierName = request.CourierName?.Trim();
        order.TrackingNote = request.TrackingNote?.Trim();
        await ChangeStatusAsync(order, OrderRules.Shipped, Actor.Seller, sellerId, request.TrackingNote, ct);
        await SaveAsync(ct);
    }

    /// <summary>FR-11.8: the tracking note can change until the order is delivered.</summary>
    public async Task SetTrackingAsync(Guid sellerId, Guid orderId, ShipRequest request, CancellationToken ct = default)
    {
        var shopId = await OwnShopIdAsync(sellerId, ct);
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.ShopId == shopId, ct) ?? throw new AppException(404, Messages.NotFound);
        if (order.Status is OrderRules.Delivered or OrderRules.Cancelled or OrderRules.AwaitingPayment)
        {
            throw new AppException(409, Messages.Conflict);
        }

        order.CourierName = request.CourierName?.Trim();
        order.TrackingNote = request.TrackingNote?.Trim();
        await SaveAsync(ct);
    }

    // ---------- admin actions ----------

    public async Task CancelByAdminAsync(Guid adminId, Guid orderId, string reason, CancellationToken ct = default)
    {
        var order = await LoadForChangeAsync(o => o.Id == orderId, ct);
        if (!OrderRules.CanMove(order.Status, OrderRules.Cancelled, Actor.Admin))
        {
            throw new AppException(409, Messages.CannotCancel);
        }

        await ChangeStatusAsync(order, OrderRules.Cancelled, Actor.Admin, adminId, reason, ct);
        notifier.Audit(adminId, "OrderCancelledByAdmin", nameof(Order), order.Id, newValue: reason);
        await SaveAsync(ct);
    }

    /// <summary>FR-18.4: an order shipped more than 10 days ago with no seller action can be closed as delivered.</summary>
    public async Task CloseStuckAsync(Guid adminId, Guid orderId, string reason, CancellationToken ct = default)
    {
        var order = await LoadForChangeAsync(o => o.Id == orderId, ct);
        var shippedAt = await LastChangeAsync(order.Id, ct);
        if (order.Status != OrderRules.Shipped || shippedAt > clock.UtcNow.AddDays(-10))
        {
            throw new AppException(409, Messages.Conflict);
        }

        await ChangeStatusAsync(order, OrderRules.Delivered, Actor.Admin, adminId, reason, ct);
        notifier.Audit(adminId, "OrderClosedByAdmin", nameof(Order), order.Id, newValue: reason);
        await SaveAsync(ct);
    }

    // ---------- the state machine and its effects ----------

    public async Task ChangeStatusAsync(Order order, string to, Actor actor, Guid? actorId, string? note, CancellationToken ct)
    {
        if (!OrderRules.CanMove(order.Status, to, actor))
        {
            throw new AppException(409, Messages.Conflict); // FR-11.10, MSG40
        }

        var from = order.Status;
        order.Status = to;
        db.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = order.Id, FromStatus = from, ToStatus = to, ChangedById = actorId, Note = note });

        switch (to)
        {
            case OrderRules.Cancelled:
                await OnCancelledAsync(order, note, ct);
                break;
            case OrderRules.Delivered:
                await OnDeliveredAsync(order, ct);
                break;
            case OrderRules.Shipped:
                notifier.Email(order.Buyer.Email, $"Order {order.OrderNumber} was shipped",
                    $"Your order is on the way. {order.CourierName} {order.TrackingNote}".Trim());
                break;
        }

        var text = to switch
        {
            OrderRules.Confirmed => "was confirmed by the seller",
            OrderRules.Processing => "is being packed",
            OrderRules.Shipped => "was shipped",
            OrderRules.Delivered => "was delivered",
            OrderRules.Cancelled => "was cancelled",
            _ => $"is now {to}",
        };
        notifier.Notify(order.BuyerId, $"Order{to}", $"Order {order.OrderNumber} {text}", note ?? $"Order {order.OrderNumber} {text}.", $"/orders/{order.Id}");
        if (to == OrderRules.Cancelled && actor != Actor.Seller)
        {
            notifier.Notify(order.Shop.OwnerId, "OrderCancelled", $"Order {order.OrderNumber} was cancelled", note ?? "The order was cancelled.", $"/seller/orders/{order.Id}");
        }
    }

    /// <summary>FR-10.5: stock, coupon use and store credit come back, and paid money is refunded at once (FR-13.8).</summary>
    private async Task OnCancelledAsync(Order order, string? reason, CancellationToken ct)
    {
        foreach (var item in order.OrderItems)
        {
            if (item.Variant is not null)
            {
                item.Variant.Stock += item.Quantity;
            }
            else
            {
                item.Product.Stock += item.Quantity;
            }
        }

        var group = order.CheckoutGroup;
        var othersOpen = group.Orders.Any(o => o.Id != order.Id && o.Status != OrderRules.Cancelled);
        if (!othersOpen && group.CouponRedemption is { ReleasedAt: null } redemption)
        {
            redemption.ReleasedAt = clock.UtcNow;
            redemption.Coupon.UsedCount = Math.Max(0, redemption.Coupon.UsedCount - 1);
        }

        if (order.CreditUsed > 0)
        {
            await ledger.PostAsync(Ledger.EventId($"credit-return:{order.Id}"), [
                new(Ledger.Receivable, -order.CreditUsed, "CreditReturned", OrderId: order.Id, RefId: order.Id),
                new(Ledger.StoreCredit, order.CreditUsed, "CreditReturned", UserId: order.BuyerId, OrderId: order.Id, RefId: order.Id),
            ], ct);
        }

        var paid = group.Payments.FirstOrDefault(p => p.Status is "Paid" or "PartlyRefunded");
        if (paid is not null && order.TotalAmount > 0)
        {
            db.Refunds.Add(new Refund
            {
                Id = Guid.CreateVersion7(), PaymentId = paid.Id, OrderId = order.Id, Amount = order.TotalAmount, Method = "OriginalMethod", Status = "Pending",
            });
        }

        if (!othersOpen)
        {
            group.Status = group.Status == "AwaitingPayment" ? (reason == "Payment expired" ? "Expired" : "Cancelled") : group.Status;
            foreach (var payment in group.Payments.Where(p => p.Status is "Initiated" or "Unpaid"))
            {
                payment.Status = reason == "Payment expired" ? "Expired" : "Cancelled";
            }
        }
    }

    /// <summary>FR-11.6 and FR-24.2: delivery records COD cash, starts the return window and books the seller's earning.</summary>
    private async Task OnDeliveredAsync(Order order, CancellationToken ct)
    {
        var now = clock.UtcNow;
        order.DeliveredAt = now;
        var group = order.CheckoutGroup;
        var cod = group.Payments.FirstOrDefault(p => p.Method == "COD");
        if (cod is not null && order.TotalAmount > 0)
        {
            await ledger.PostAsync(Ledger.EventId($"cash:{order.Id}"), [
                new(Ledger.PlatformCash, -order.TotalAmount, "OrderReceived", OrderId: order.Id, RefId: cod.Id),
                new(Ledger.Receivable, order.TotalAmount, "OrderReceived", OrderId: order.Id, RefId: cod.Id),
            ], ct);
            if (group.Orders.All(o => o.Id == order.Id || o.Status is OrderRules.Delivered or OrderRules.Cancelled))
            {
                cod.Status = "Paid";
                cod.PaidAt = now;
            }
        }

        // Earning = items - commission + delivery fee (FR-15.2). The coupon is the platform's cost (BR-45).
        var held = order.TotalAmount + order.CreditUsed;
        var earning = order.ItemsTotal - order.CommissionAmount + order.DeliveryFee;
        await ledger.PostAsync(Ledger.EventId($"earning:{order.Id}"), [
            new(Ledger.Receivable, -held, "EarningBooked", OrderId: order.Id, RefId: order.Id),
            new(Ledger.SellerPending, earning, "EarningPending", ShopId: order.ShopId, OrderId: order.Id, RefId: order.Id),
            new(Ledger.Commission, order.CommissionAmount - order.DiscountAmount, "Commission", OrderId: order.Id, RefId: order.Id),
        ], ct);
        notifier.Email(order.Buyer.Email, $"Order {order.OrderNumber} was delivered",
            $"Your order was delivered. You can ask for a return within {await settings.GetIntAsync("returnWindowDays", 7, ct)} days.");
    }

    // ---------- helpers ----------

    public IQueryable<Order> ChangeQuery() => db.Orders
        .Include(o => o.Buyer).Include(o => o.Shop)
        .Include(o => o.OrderItems).ThenInclude(i => i.Variant)
        .Include(o => o.OrderItems).ThenInclude(i => i.Product)
        .Include(o => o.CheckoutGroup).ThenInclude(g => g.Orders)
        .Include(o => o.CheckoutGroup).ThenInclude(g => g.Payments)
        .Include(o => o.CheckoutGroup).ThenInclude(g => g.CouponRedemption!).ThenInclude(r => r.Coupon)
        .AsSplitQuery();

    private async Task<Order> LoadForChangeAsync(System.Linq.Expressions.Expression<Func<Order, bool>> filter, CancellationToken ct) =>
        await ChangeQuery().IgnoreQueryFilters().Where(o => !o.IsDeleted).FirstOrDefaultAsync(filter, ct) ?? throw new AppException(404, Messages.NotFound);

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new AppException(409, Messages.Conflict); // NFR-13: two people changed the order at once
        }
    }

    public async Task<DateTime> LastChangeAsync(Guid orderId, CancellationToken ct) =>
        await db.OrderStatusHistories.Where(h => h.OrderId == orderId).MaxAsync(h => (DateTime?)h.CreatedAt, ct) ?? DateTime.MinValue;

    private async Task<Guid> OwnShopIdAsync(Guid sellerId, CancellationToken ct) =>
        await db.Shops.Where(s => s.OwnerId == sellerId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct) ?? throw new AppException(403, Messages.NotAllowed);

    private IQueryable<Order> DetailQuery() => db.Orders.AsNoTracking()
        .Include(o => o.Shop).Include(o => o.Buyer)
        .Include(o => o.OrderItems).ThenInclude(i => i.Review)
        .Include(o => o.OrderStatusHistories)
        .Include(o => o.Refunds)
        .Include(o => o.CheckoutGroup).ThenInclude(g => g.Payments)
        .AsSplitQuery();

    private async Task<PagedResult<OrderListItemDto>> ListAsync(IQueryable<Order> query, string? status, int page, Actor actor, CancellationToken ct)
    {
        page = Math.Max(1, page);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(o => o.Status == status);
        }

        var total = await query.CountAsync(ct);
        var rows = await query.AsNoTracking().OrderByDescending(o => o.CreatedAt).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.CreatedAt, Shop = o.Shop.Name, Buyer = o.Buyer.FullName, o.TotalAmount, o.Status, o.CreditUsed, o.CheckoutGroupId,
                Method = o.CheckoutGroup.PaymentMethod, o.CheckoutGroup.ExpiresAt,
                Payment = o.CheckoutGroup.Payments.OrderByDescending(p => p.CreatedAt).Select(p => p.Status).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var now = clock.UtcNow;
        var items = rows.Select(r => new OrderListItemDto(r.Id, r.OrderNumber, r.CreatedAt, r.Shop, r.Buyer, r.TotalAmount, r.Status,
            r.Payment ?? (r.CreditUsed > 0 ? "PaidByCredit" : "Unpaid"), r.Method,
            r.CheckoutGroupId, r.Status == OrderRules.AwaitingPayment && r.ExpiresAt > now && actor == Actor.Buyer,
            OrderRules.CanMove(r.Status, OrderRules.Cancelled, actor))).ToList();
        return new PagedResult<OrderListItemDto>(items, page, PageSize, total);
    }

    private async Task<OrderDetailDto> ToDetailAsync(Order o, Actor actor, CancellationToken ct)
    {
        var payments = o.CheckoutGroup.Payments.OrderByDescending(p => p.CreatedAt).ToList();
        var actions = OrderRules.NextFor(o.Status, actor).ToList();
        if (actor == Actor.Buyer && o.Status == OrderRules.AwaitingPayment && o.CheckoutGroup.ExpiresAt > clock.UtcNow)
        {
            actions.Add("Pay");
        }

        if (actor == Actor.Admin && o.Status == OrderRules.Shipped && await LastChangeAsync(o.Id, ct) <= clock.UtcNow.AddDays(-10))
        {
            actions.Add("CloseAsDelivered");
        }

        if (actor == Actor.Buyer && o.Status is OrderRules.Confirmed or OrderRules.Processing or OrderRules.Shipped or OrderRules.Delivered)
        {
            actions.Add("Invoice"); // BR-42
        }

        // The seller sees the buyer's contact only once the order is really theirs to fulfil (FR-11.2).
        return new OrderDetailDto(o.Id, o.OrderNumber, o.CreatedAt, o.Status, o.ShopId, o.Shop.Name, o.Buyer.FullName, o.ShipName, o.ShipPhone,
            o.ShipAddress, o.ItemsTotal, o.DeliveryFee, o.DiscountAmount, o.CreditUsed, o.TotalAmount, o.CourierName, o.TrackingNote, o.DeliveredAt,
            o.CheckoutGroup.PaymentMethod, payments.FirstOrDefault()?.Status ?? (o.CreditUsed > 0 ? "PaidByCredit" : "Unpaid"),
            o.OrderItems.Select(i => new OrderItemDto(i.Id, i.ProductId, i.ProductName, i.VariantText, i.UnitPrice, i.Quantity, i.ReturnedQuantity, i.Review is not null)).ToList(),
            o.OrderStatusHistories.OrderBy(h => h.CreatedAt).Select(h => new StatusChangeDto(h.FromStatus, h.ToStatus, h.CreatedAt, h.Note)).ToList(),
            actor == Actor.Seller ? [] : payments.Select(p => new PaymentInfoDto(p.Id, p.Method, p.Amount, p.Status, p.TransactionId, p.CreatedAt, p.PaidAt)).ToList(),
            o.Refunds.OrderBy(r => r.CreatedAt).Select(r => new RefundInfoDto(r.Id, r.Amount, r.Method, r.Status, r.CreatedAt)).ToList(),
            actions, o.Status == OrderRules.AwaitingPayment ? o.CheckoutGroup.ExpiresAt : null);
    }
}
