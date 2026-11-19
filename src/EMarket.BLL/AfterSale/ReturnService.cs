using System.Text.Json;
using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.BLL.Orders;
using EMarket.BLL.Payments;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.AfterSale;
using EMarket.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.AfterSale;

/// <summary>FR-13: returns and refunds after delivery, and "not received" reports.</summary>
public sealed class ReturnService(AppDbContext db, IFileStorage files, Ledger ledger, Notifier notifier, ISettingsReader settings, IClock clock, RefundService refunds)
{
    private readonly List<Guid> newRefunds = [];

    public const int MaxPhotos = 3;
    private const int SellerAnswerDays = 3;
    private static readonly string[] OpenStatuses = ["Requested", "Approved", "ItemReceived"];
    private static readonly string[] SellerFaultReasons = ["Damaged", "WrongItem", "NotAsDescribed", "NotReceived"];

    public async Task<Guid> RequestAsync(Guid buyerId, Guid orderId, ReturnRequestInput input, IReadOnlyList<UploadFile> photos, CancellationToken ct = default)
    {
        if (photos.Count > MaxPhotos)
        {
            throw new AppException(400, Messages.BadFile);
        }

        var order = await db.Orders.Include(o => o.OrderItems).Include(o => o.Shop).Include(o => o.CheckoutGroup).ThenInclude(g => g.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, ct) ?? throw new AppException(404, Messages.NotFound);
        await CheckWindowAsync(order, input.Reason, ct);

        var request = new ReturnRequest
        {
            Id = Guid.CreateVersion7(),
            OrderId = order.Id,
            BuyerId = buyerId,
            Reason = input.Reason,
            Description = input.Description?.Trim() ?? "",
            Status = "Requested",
            // BR-21: money paid in cash, or only with store credit, can only go back as store credit.
            RefundMethod = order.CheckoutGroup.Payments.Any(p => p.Method != "COD" && p.Status is "Paid" or "PartlyRefunded") ? input.RefundMethod : "StoreCredit",
        };

        var openQuantities = await OpenQuantitiesAsync(order.Id, ct);
        foreach (var line in input.Items.GroupBy(i => i.OrderItemId!.Value))
        {
            var item = order.OrderItems.FirstOrDefault(i => i.Id == line.Key) ?? throw new AppException(400, Messages.Invalid);
            var quantity = line.Sum(l => l.Quantity);
            // BR-20: one open request per item, and never more than delivered minus already returned.
            if (openQuantities.ContainsKey(item.Id) || quantity > item.Quantity - item.ReturnedQuantity)
            {
                throw new AppException(409, Messages.Conflict);
            }

            request.ReturnItems.Add(new ReturnItem { ReturnRequestId = request.Id, OrderItemId = item.Id, Quantity = quantity });
        }

        var paths = new List<string>();
        foreach (var photo in photos)
        {
            paths.Add(await files.SaveImageAsync(photo, "returns", ct));
        }

        request.PhotoPaths = paths.Count == 0 ? null : JsonSerializer.Serialize(paths);
        db.ReturnRequests.Add(request);
        notifier.Notify(order.Shop.OwnerId, "ReturnRequested", "New return request",
            $"Order {order.OrderNumber}: {input.Reason}. Please answer within {SellerAnswerDays} days.", $"/seller/returns/{request.Id}");
        notifier.Notify(buyerId, "ReturnRequested", "Return request sent", Messages.ReturnSent, $"/returns/{request.Id}");
        await db.SaveChangesAsync(ct);
        return request.Id;
    }

    public async Task SellerDecideAsync(Guid sellerId, Guid requestId, DecisionRequest decision, CancellationToken ct = default)
    {
        var request = await LoadAsync(requestId, ct);
        if (request.Order.Shop.OwnerId != sellerId)
        {
            throw new AppException(404, Messages.NotFound);
        }

        if (request.Status != "Requested" || request.EscalatedAt is not null)
        {
            throw new AppException(409, Messages.Conflict);
        }

        await DecideAsync(request, sellerId, decision, byAdmin: false, ct);
    }

    /// <summary>FR-13.7: the buyer may send a request the seller rejected to the admin once.</summary>
    public async Task EscalateAsync(Guid buyerId, Guid requestId, CancellationToken ct = default)
    {
        var request = await LoadAsync(requestId, ct);
        if (request.BuyerId != buyerId)
        {
            throw new AppException(404, Messages.NotFound);
        }

        if (request.Status != "Rejected" || request.EscalatedAt is not null || await DecidedByAdminAsync(request, ct))
        {
            throw new AppException(409, Messages.Conflict);
        }

        request.EscalatedAt = clock.UtcNow;
        request.Status = "Requested";
        await notifier.NotifyAdminsAsync("ReturnEscalated", "Return sent to admin", $"Order {request.Order.OrderNumber}: the buyer asks you to decide.", $"/admin/returns/{request.Id}", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The admin's decision is final and may overrule the seller (FR-13.7).</summary>
    public async Task AdminDecideAsync(Guid adminId, Guid requestId, DecisionRequest decision, CancellationToken ct = default)
    {
        var request = await LoadAsync(requestId, ct);
        if (request.Status is not ("Requested" or "Rejected") || await DecidedByAdminAsync(request, ct))
        {
            throw new AppException(409, Messages.Conflict);
        }

        await DecideAsync(request, adminId, decision, byAdmin: true, ct);
    }

    /// <summary>FR-13.9: the refund starts when the seller says the goods came back.</summary>
    public async Task ItemReceivedAsync(Guid sellerId, Guid requestId, bool resellable, CancellationToken ct = default)
    {
        var request = await LoadAsync(requestId, ct);
        if (request.Order.Shop.OwnerId != sellerId)
        {
            throw new AppException(404, Messages.NotFound);
        }

        if (request.Status != "Approved" || request.Reason == "NotReceived")
        {
            throw new AppException(409, Messages.Conflict);
        }

        request.Status = "ItemReceived";
        if (resellable)
        {
            foreach (var line in request.ReturnItems)
            {
                if (line.OrderItem.Variant is { } variant)
                {
                    variant.Stock += line.Quantity;
                }
                else
                {
                    line.OrderItem.Product.Stock += line.Quantity;
                }
            }
        }

        notifier.Notify(request.BuyerId, "ReturnItemReceived", "The seller got your return", "Your refund is on its way.", $"/returns/{request.Id}");
        await StartRefundAsync(request, ct);
        await db.SaveChangesAsync(ct);
        await RunNewRefundsAsync(ct);
    }

    public async Task<PagedResult<ReturnDto>> ListAsync(Guid? buyerId, Guid? sellerId, bool adminQueue, string? status, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = Query();
        if (buyerId is not null)
        {
            query = query.Where(r => r.BuyerId == buyerId);
        }

        if (sellerId is not null)
        {
            query = query.Where(r => r.Order.Shop.OwnerId == sellerId);
        }

        if (adminQueue && string.IsNullOrWhiteSpace(status))
        {
            // FR-13.6: a request the seller left for 3 days goes to the admin, as does an escalated one.
            var overdue = clock.UtcNow.AddDays(-SellerAnswerDays);
            query = query.Where(r => r.Status == "Requested" && (r.EscalatedAt != null || r.CreatedAt <= overdue));
        }
        else if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status == status);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var role = adminQueue ? "admin" : sellerId is not null ? "seller" : "buyer";
        return new PagedResult<ReturnDto>(items.Select(r => ToDto(r, role)).ToList(), page, pageSize, total);
    }

    public async Task<ReturnDto> GetAsync(Guid requestId, Guid userId, string role, CancellationToken ct = default)
    {
        var request = await Query().FirstOrDefaultAsync(r => r.Id == requestId, ct) ?? throw new AppException(404, Messages.NotFound);
        var allowed = role == "admin" || (role == "buyer" && request.BuyerId == userId) || (role == "seller" && request.Order.Shop.OwnerId == userId);
        return allowed ? ToDto(request, role) : throw new AppException(404, Messages.NotFound);
    }

    /// <summary>Photos of a return are private: only the buyer, the shop's seller and admins may see them.</summary>
    public async Task<Stream> OpenPhotoAsync(Guid requestId, int index, Guid userId, string role, CancellationToken ct = default)
    {
        var dto = await GetAsync(requestId, userId, role, ct);
        var request = await db.ReturnRequests.AsNoTracking().FirstAsync(r => r.Id == dto.Id, ct);
        var paths = request.PhotoPaths is null ? [] : JsonSerializer.Deserialize<List<string>>(request.PhotoPaths) ?? [];
        return index >= 0 && index < paths.Count && files.Open(paths[index]) is { } stream ? stream : throw new AppException(404, Messages.NotFound);
    }

    /// <summary>FR-13.5: item price × quantity minus its coupon share; the delivery fee only when the seller is at fault or all is returned.</summary>
    public static decimal RefundAmount(Order order, IEnumerable<(OrderItem Item, int Quantity)> lines, string reason)
    {
        var list = lines.ToList();
        var itemsPart = list.Sum(l => l.Item.UnitPrice * l.Quantity);
        var couponShare = order.ItemsTotal == 0 ? 0 : Math.Round(order.DiscountAmount * itemsPart / order.ItemsTotal, 2, MidpointRounding.AwayFromZero);
        var wholeOrder = order.OrderItems.All(i => list.Where(l => l.Item.Id == i.Id).Sum(l => l.Quantity) + i.ReturnedQuantity >= i.Quantity);
        var fee = SellerFaultReasons.Contains(reason) || wholeOrder ? order.DeliveryFee : 0;
        return itemsPart - couponShare + fee;
    }

    private async Task DecideAsync(ReturnRequest request, Guid deciderId, DecisionRequest decision, bool byAdmin, CancellationToken ct)
    {
        request.DecidedById = deciderId;
        if (!decision.Approve)
        {
            var reason = decision.Reason?.Trim();
            if (reason is null || reason.Length < 10)
            {
                throw new AppException(400, Messages.Invalid);
            }

            request.Status = "Rejected";
            request.RejectionReason = reason;
            notifier.Notify(request.BuyerId, "ReturnRejected", "Return request rejected", reason, $"/returns/{request.Id}");
            notifier.Email(request.Buyer.Email, "Your return request", $"Your return for order {request.Order.OrderNumber} was rejected. Reason: {reason}"
                + (byAdmin || request.EscalatedAt is not null ? "" : " You may ask the admin to decide once."));
        }
        else
        {
            request.Status = "Approved";
            request.RejectionReason = null;
            notifier.Notify(request.BuyerId, "ReturnApproved", "Return request approved",
                request.Reason == "NotReceived" ? "Your refund is on its way." : "Please send the goods back to the seller as you agree with them.", $"/returns/{request.Id}");
            notifier.Email(request.Buyer.Email, "Your return request", $"Your return for order {request.Order.OrderNumber} was approved.");
            if (request.Reason == "NotReceived")
            {
                await StartRefundAsync(request, ct); // no goods come back (FR-13.9)
            }
        }

        if (byAdmin)
        {
            notifier.Audit(deciderId, decision.Approve ? "ReturnApprovedByAdmin" : "ReturnRejectedByAdmin", nameof(ReturnRequest), request.Id, newValue: decision.Reason);
            notifier.Notify(request.Order.Shop.OwnerId, "ReturnDecided", "Admin decided a return", $"Order {request.Order.OrderNumber}: {(decision.Approve ? "approved" : "rejected")}.", $"/seller/returns/{request.Id}");
        }

        await db.SaveChangesAsync(ct);
        await RunNewRefundsAsync(ct);
    }

    private async Task StartRefundAsync(ReturnRequest request, CancellationToken ct)
    {
        var order = request.Order;
        var lines = request.ReturnItems.Select(r => (r.OrderItem, r.Quantity)).ToList();
        var amount = RefundAmount(order, lines, request.Reason);

        // FR-13.13: the total refunded for an order never exceeds what was paid for it.
        var alreadyRefunded = await db.Refunds.Where(r => r.OrderId == order.Id && r.Status != "Failed").SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
        amount = Math.Min(amount, order.TotalAmount + order.CreditUsed - alreadyRefunded);
        if (amount <= 0)
        {
            throw new AppException(409, Messages.Conflict);
        }

        foreach (var (item, quantity) in lines)
        {
            item.ReturnedQuantity += quantity;
        }

        var payment = order.CheckoutGroup.Payments.FirstOrDefault(p => p.Status is "Paid" or "PartlyRefunded");
        if (payment is not null)
        {
            var refund = new Refund
            {
                Id = Guid.CreateVersion7(), PaymentId = payment.Id, OrderId = order.Id, ReturnRequestId = request.Id, Amount = amount,
                Method = request.RefundMethod ?? "StoreCredit", Status = "Pending",
            };
            db.Refunds.Add(refund);
            newRefunds.Add(refund.Id);
            return;
        }

        // Paid only with store credit: there is no payment row, so the credit goes straight back through the ledger.
        var released = await db.LedgerEntries.AnyAsync(e => e.OrderId == order.Id && e.Kind == "EarningReleased", ct);
        var share = amount / (order.TotalAmount + order.CreditUsed);
        var commission = Math.Round(order.CommissionAmount * share, 2, MidpointRounding.AwayFromZero);
        var coupon = Math.Round(order.DiscountAmount * share, 2, MidpointRounding.AwayFromZero);
        await ledger.PostAsync(Ledger.EventId($"return-credit:{request.Id}"), [
            new(released ? Ledger.SellerAvailable : Ledger.SellerPending, -(amount + coupon - commission), "RefundReversal", ShopId: order.ShopId, OrderId: order.Id, RefId: request.Id),
            new(Ledger.Commission, -(commission - coupon), "RefundReversal", OrderId: order.Id, RefId: request.Id),
            new(Ledger.StoreCredit, amount, "RefundToCredit", UserId: order.BuyerId, OrderId: order.Id, RefId: request.Id),
        ], ct);
        request.Status = "Refunded";
    }

    // FR-13.10: store credit refunds finish at once; a gateway refund that fails here stays for the job and the admin retry.
    private async Task RunNewRefundsAsync(CancellationToken ct)
    {
        foreach (var id in newRefunds.ToList())
        {
            newRefunds.Remove(id);
            await refunds.ExecuteAsync(id, ct);
        }
    }

    private async Task CheckWindowAsync(Order order, string reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var lastChange = await db.OrderStatusHistories.Where(h => h.OrderId == order.Id).MaxAsync(h => (DateTime?)h.CreatedAt, ct) ?? order.CreatedAt;
        if (reason == "NotReceived")
        {
            // BR-44: shipped for 3 days or more, or delivered, and within 14 days of the last status change.
            var ok = (order.Status == OrderRules.Shipped && lastChange <= now.AddDays(-3) || order.Status == OrderRules.Delivered)
                && lastChange >= now.AddDays(-14);
            if (!ok)
            {
                throw new AppException(409, Messages.ReturnWindowOver);
            }

            return;
        }

        var days = await settings.GetIntAsync("returnWindowDays", 7, ct);
        if (order.Status != OrderRules.Delivered || order.DeliveredAt is null || order.DeliveredAt.Value.AddDays(days) < now)
        {
            throw new AppException(409, Messages.ReturnWindowOver); // BR-19, MSG23
        }
    }

    private async Task<Dictionary<Guid, int>> OpenQuantitiesAsync(Guid orderId, CancellationToken ct) =>
        await db.ReturnItems.Where(i => i.ReturnRequest.OrderId == orderId && OpenStatuses.Contains(i.ReturnRequest.Status))
            .GroupBy(i => i.OrderItemId).Select(g => new { g.Key, Total = g.Sum(i => i.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Total, ct);

    private async Task<bool> DecidedByAdminAsync(ReturnRequest request, CancellationToken ct) =>
        await db.AuditLogs.AnyAsync(a => a.EntityId == request.Id && (a.Action == "ReturnApprovedByAdmin" || a.Action == "ReturnRejectedByAdmin"), ct);

    private async Task<ReturnRequest> LoadAsync(Guid requestId, CancellationToken ct) =>
        await db.ReturnRequests.Include(r => r.Buyer)
            .Include(r => r.Order).ThenInclude(o => o.Shop)
            .Include(r => r.Order).ThenInclude(o => o.OrderItems)
            .Include(r => r.Order).ThenInclude(o => o.CheckoutGroup).ThenInclude(g => g.Payments)
            .Include(r => r.ReturnItems).ThenInclude(i => i.OrderItem).ThenInclude(i => i.Variant)
            .Include(r => r.ReturnItems).ThenInclude(i => i.OrderItem).ThenInclude(i => i.Product)
            .AsSplitQuery().FirstOrDefaultAsync(r => r.Id == requestId, ct) ?? throw new AppException(404, Messages.NotFound);

    private IQueryable<ReturnRequest> Query() => db.ReturnRequests.AsNoTracking()
        .Include(r => r.Buyer).Include(r => r.Order).ThenInclude(o => o.Shop).Include(r => r.Order).ThenInclude(o => o.OrderItems)
        .Include(r => r.ReturnItems).ThenInclude(i => i.OrderItem).AsSplitQuery();

    private static ReturnDto ToDto(ReturnRequest r, string role)
    {
        var lines = r.ReturnItems.Select(i => (i.OrderItem, i.Quantity)).ToList();
        var photos = r.PhotoPaths is null ? [] : JsonSerializer.Deserialize<List<string>>(r.PhotoPaths) ?? [];
        var actions = new List<string>();
        if (role == "seller" && r.Status == "Requested" && r.EscalatedAt is null)
        {
            actions.AddRange(["Approve", "Reject"]);
        }

        if (role == "seller" && r.Status == "Approved" && r.Reason != "NotReceived")
        {
            actions.Add("ItemReceived");
        }

        if (role == "buyer" && r.Status == "Rejected" && r.EscalatedAt is null)
        {
            actions.Add("SendToAdmin");
        }

        if (role == "admin" && r.Status is "Requested" or "Rejected")
        {
            actions.AddRange(["Approve", "Reject"]);
        }

        return new ReturnDto(r.Id, r.OrderId, r.Order.OrderNumber, r.Buyer.FullName, r.Order.Shop.Name, r.Reason, r.Description, r.Status,
            r.RejectionReason, r.RefundMethod, r.EscalatedAt is not null, RefundAmount(r.Order, lines, r.Reason), r.CreatedAt,
            r.ReturnItems.Select(i => new ReturnLineDto(i.OrderItemId, i.OrderItem.ProductName, i.OrderItem.VariantText, i.Quantity, i.OrderItem.UnitPrice)).ToList(),
            photos.Select((_, index) => $"/api/v1/returns/{r.Id}/photos/{index}").ToList(), actions);
    }
}
