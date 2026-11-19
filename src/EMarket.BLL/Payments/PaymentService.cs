using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.BLL.Orders;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EMarket.BLL.Payments;

/// <summary>
/// FR-9: online payment. Orders are saved first (Awaiting Payment); money is booked only after the gateway
/// confirms the transaction server to server, once per transaction id (NFR-38).
/// </summary>
public sealed class PaymentService(
    AppDbContext db, IPaymentGateway gateway, OrderService orders, Ledger ledger, Notifier notifier, IClock clock, ILogger<PaymentService> logger)
{
    /// <summary>Pay now (FR-9.8): reuses an open attempt, otherwise starts a new one with a new transaction id.</summary>
    public async Task<PaymentStartDto> StartAsync(Guid buyerId, Guid checkoutGroupId, CancellationToken ct = default)
    {
        var group = await db.CheckoutGroups.Include(g => g.Payments).Include(g => g.Buyer)
            .Include(g => g.Orders).FirstOrDefaultAsync(g => g.Id == checkoutGroupId && g.BuyerId == buyerId, ct)
            ?? throw new AppException(404, Messages.NotFound);
        if (group.Status != "AwaitingPayment" || group.ExpiresAt <= clock.UtcNow)
        {
            throw new AppException(409, Messages.Conflict);
        }

        var payment = group.Payments.FirstOrDefault(p => p.Status == "Initiated");
        if (payment is null)
        {
            payment = new Payment
            {
                Id = Guid.CreateVersion7(), CheckoutGroupId = group.Id, BuyerId = buyerId, Method = group.PaymentMethod, Amount = group.PayableTotal,
                Status = "Initiated", TransactionId = $"EM{Guid.NewGuid():N}"[..30],
            };
            db.Payments.Add(payment);
            await db.SaveChangesAsync(ct);
        }

        var firstOrder = group.Orders.OrderBy(o => o.CreatedAt).First();
        var session = await gateway.StartAsync(payment.TransactionId!, payment.Amount,
            new GatewayCustomer(group.Buyer.FullName, group.Buyer.Email, group.Buyer.Phone, firstOrder.ShipAddress), payment.Method, ct);
        return new PaymentStartDto(session.RedirectUrl, payment.TransactionId!, group.ExpiresAt!.Value);
    }

    /// <summary>
    /// Handles any callback (success, fail, cancel, IPN). The callback itself is never trusted: the gateway is asked
    /// about the transaction, and the amount and currency must match what we asked for.
    /// </summary>
    public async Task<string> SettleAsync(string transactionId, CancellationToken ct = default)
    {
        var payment = await db.Payments.Include(p => p.CheckoutGroup).FirstOrDefaultAsync(p => p.TransactionId == transactionId, ct);
        if (payment is null)
        {
            logger.LogWarning("Payment callback for unknown transaction {TransactionId}", transactionId);
            return "Unknown";
        }

        if (payment.Status != "Initiated")
        {
            return payment.Status; // already settled: the same transaction is handled once
        }

        var check = await gateway.CheckAsync(transactionId, ct);
        payment.LastCheckedAt = clock.UtcNow;
        payment.GatewayStatus = check?.Status is { } s ? s[..Math.Min(30, s.Length)] : null;
        if (check is { IsPaid: true })
        {
            if (check.Amount != payment.Amount || check.Currency != "BDT" || check.TransactionId != transactionId)
            {
                logger.LogWarning("Payment {TransactionId} rejected: gateway amount {Amount} {Currency} does not match", transactionId, check.Amount, check.Currency);
                await db.SaveChangesAsync(ct);
                return "Mismatch";
            }

            await MarkPaidAsync(payment, ct);
        }
        else if (check is { IsFailed: true } && check.Status != "UNATTEMPTED")
        {
            // FR-9.7: the attempt is marked, but the order waits for the rest of the payment window.
            payment.Status = check.Status == "CANCELLED" ? "Cancelled" : "Failed";
            notifier.Notify(payment.BuyerId, "PaymentFailed", "Payment not completed",
                payment.Status == "Cancelled" ? Messages.PaymentCancelled : Messages.PaymentFailed, "/orders");
        }

        await db.SaveChangesAsync(ct);
        return payment.Status;
    }

    /// <summary>FR-9.6: payment Paid, orders move to Pending, sellers are told, and the money is booked per order (FR-9.13).</summary>
    private async Task MarkPaidAsync(Payment payment, CancellationToken ct)
    {
        payment.Status = "Paid";
        payment.PaidAt = clock.UtcNow;
        var groupOrders = await orders.ChangeQuery().Where(o => o.CheckoutGroupId == payment.CheckoutGroupId).ToListAsync(ct);
        var group = groupOrders[0].CheckoutGroup;
        group.Status = "Placed";
        group.ExpiresAt = null;

        var parts = Ledger.Split(payment.Amount, groupOrders.Select(o => o.TotalAmount).ToList());
        for (var i = 0; i < groupOrders.Count; i++)
        {
            var order = groupOrders[i];
            await ledger.PostAsync(Ledger.EventId($"paid:{payment.Id}:{order.Id}"), [
                new(Ledger.PlatformCash, -parts[i], "OrderReceived", OrderId: order.Id, RefId: payment.Id),
                new(Ledger.Receivable, parts[i], "OrderReceived", OrderId: order.Id, RefId: payment.Id),
            ], ct);
            if (order.Status == OrderRules.AwaitingPayment)
            {
                await orders.ChangeStatusAsync(order, OrderRules.Pending, Actor.System, null, "Payment confirmed", ct);
                notifier.Notify(order.Shop.OwnerId, "OrderPlaced", "New order", $"Order {order.OrderNumber} is paid and waiting for you.", $"/seller/orders/{order.Id}");
            }
        }

        var buyer = groupOrders[0].Buyer;
        notifier.Email(buyer.Email, "Payment received", $"We received Tk {payment.Amount:N2} for orders {string.Join(", ", groupOrders.Select(o => o.OrderNumber))}.");
    }

    /// <summary>FR-9.4: unpaid online orders expire after the payment window; a last gateway check runs first.</summary>
    public async Task<int> ExpireUnpaidAsync(CancellationToken ct = default)
    {
        var due = await db.CheckoutGroups.Where(g => g.Status == "AwaitingPayment" && g.ExpiresAt < clock.UtcNow).Select(g => g.Id).Take(100).ToListAsync(ct);
        foreach (var groupId in due)
        {
            foreach (var open in await db.Payments.Where(p => p.CheckoutGroupId == groupId && p.Status == "Initiated").Select(p => p.TransactionId!).ToListAsync(ct))
            {
                await SettleAsync(open, ct);
            }

            var groupOrders = await orders.ChangeQuery().Where(o => o.CheckoutGroupId == groupId && o.Status == OrderRules.AwaitingPayment).ToListAsync(ct);
            foreach (var order in groupOrders)
            {
                await orders.ChangeStatusAsync(order, OrderRules.Cancelled, Actor.System, null, "Payment expired", ct);
            }

            await db.SaveChangesAsync(ct);
        }

        return due.Count;
    }

    /// <summary>FR-9.9: every 10 minutes, ask the gateway about attempts older than 5 minutes so a lost callback is corrected.</summary>
    public async Task<int> ReconcileAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddMinutes(-5);
        var open = await db.Payments.Where(p => p.Status == "Initiated" && p.TransactionId != null && p.CreatedAt < cutoff)
            .OrderBy(p => p.LastCheckedAt).Select(p => p.TransactionId!).Take(100).ToListAsync(ct);
        foreach (var transactionId in open)
        {
            await SettleAsync(transactionId, ct);
        }

        return open.Count;
    }
}

/// <summary>FR-13.10: refunds. Store credit finishes at once; card and wallet refunds go through the gateway with the refund id as reference.</summary>
public sealed class RefundService(AppDbContext db, IPaymentGateway gateway, Ledger ledger, Notifier notifier, ILogger<RefundService> logger)
{
    public async Task<int> ProcessPendingAsync(CancellationToken ct = default)
    {
        var pending = await db.Refunds.Where(r => r.Status == "Pending").OrderBy(r => r.CreatedAt).Select(r => r.Id).Take(50).ToListAsync(ct);
        foreach (var id in pending)
        {
            await ExecuteAsync(id, ct);
        }

        return pending.Count;
    }

    public async Task ExecuteAsync(Guid refundId, CancellationToken ct = default)
    {
        var refund = await db.Refunds.Include(r => r.Payment).Include(r => r.Order).ThenInclude(o => o.Buyer)
            .IgnoreQueryFilters().FirstAsync(r => r.Id == refundId, ct);
        if (refund.Status != "Pending")
        {
            return;
        }

        // BR-21: COD cash, and any refund the buyer chose as credit, go to store credit.
        var toCredit = refund.Method == "StoreCredit" || refund.Payment.Method == "COD";
        if (!toCredit)
        {
            var check = await gateway.CheckAsync(refund.Payment.TransactionId!, ct);
            var result = check?.BankTransactionId is { } bankId
                ? await gateway.RefundAsync(bankId, refund.Amount, refund.Id, ct)
                : new GatewayRefundResult(false, null, "The gateway does not know this payment.");
            if (!result.Success)
            {
                refund.Status = "Failed";
                logger.LogWarning("Refund {RefundId} failed: {Error}", refund.Id, result.Error);
                await notifier.NotifyAdminsAsync("RefundFailed", "A refund failed", $"Order {refund.Order.OrderNumber}: {result.Error}", "/admin/payments", ct);
                await db.SaveChangesAsync(ct);
                return;
            }

            refund.GatewayRefundId = result.RefundReference;
        }

        refund.Status = "Completed";
        var lines = new List<Ledger.Line>();
        if (refund.Order.DeliveredAt is null)
        {
            lines.Add(new(Ledger.Receivable, -refund.Amount, "Refund", OrderId: refund.OrderId, RefId: refund.Id));
        }
        else
        {
            // Once the earning was released, the reversal comes out of the available balance, which may go negative (FR-15.8).
            var released = await db.LedgerEntries.AnyAsync(e => e.OrderId == refund.OrderId && e.Kind == "EarningReleased", ct);
            lines.AddRange(EarningReversal(refund, released));
        }

        lines.Add(toCredit
            ? new(Ledger.StoreCredit, refund.Amount, "RefundToCredit", UserId: refund.Order.BuyerId, OrderId: refund.OrderId, RefId: refund.Id)
            : new(Ledger.PlatformCash, refund.Amount, "RefundPaidOut", OrderId: refund.OrderId, RefId: refund.Id));
        await ledger.PostAsync(Ledger.EventId($"refund:{refund.Id}"), lines, ct);

        var refundedSoFar = await db.Refunds.Where(r => r.PaymentId == refund.PaymentId && r.Status == "Completed" && r.Id != refund.Id).SumAsync(r => r.Amount, ct) + refund.Amount;
        refund.Payment.Status = refundedSoFar >= refund.Payment.Amount ? "Refunded" : "PartlyRefunded";
        notifier.Notify(refund.Order.BuyerId, "RefundCompleted", "Refund completed",
            $"Tk {refund.Amount:N2} for order {refund.Order.OrderNumber} was refunded {(toCredit ? "to your store credit" : "to your payment method")}.", $"/orders/{refund.OrderId}");
        notifier.Email(refund.Order.Buyer.Email, "Your EMarket refund", $"Tk {refund.Amount:N2} for order {refund.Order.OrderNumber} was refunded.");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Admin retry of a failed refund (FR-18.5). The same refund id is sent again, so the gateway cannot pay twice.</summary>
    public async Task RetryAsync(Guid adminId, Guid refundId, CancellationToken ct = default)
    {
        var refund = await db.Refunds.FirstOrDefaultAsync(r => r.Id == refundId, ct) ?? throw new AppException(404, Messages.NotFound);
        if (refund.Status != "Failed")
        {
            throw new AppException(409, Messages.Conflict);
        }

        refund.Status = "Pending";
        notifier.Audit(adminId, "RefundRetried", nameof(Refund), refund.Id);
        await db.SaveChangesAsync(ct);
        await ExecuteAsync(refund.Id, ct);
    }

    /// <summary>
    /// FR-24.3: after delivery the refund reverses the seller's earning and the commission for the refunded part,
    /// in proportion to what the buyer paid for the order.
    /// </summary>
    private static IEnumerable<Ledger.Line> EarningReversal(Refund refund, bool released)
    {
        var order = refund.Order;
        var paidTotal = order.TotalAmount + order.CreditUsed;
        var share = paidTotal == 0 ? 0 : refund.Amount / paidTotal;
        var commission = Math.Round(order.CommissionAmount * share, 2, MidpointRounding.AwayFromZero);
        var coupon = Math.Round(order.DiscountAmount * share, 2, MidpointRounding.AwayFromZero);
        var sellerPart = refund.Amount + coupon - commission;
        yield return new(released ? Ledger.SellerAvailable : Ledger.SellerPending, -sellerPart, "RefundReversal", ShopId: order.ShopId, OrderId: order.Id, RefId: refund.Id);
        yield return new(Ledger.Commission, -(commission - coupon), "RefundReversal", OrderId: order.Id, RefId: refund.Id);
    }
}
