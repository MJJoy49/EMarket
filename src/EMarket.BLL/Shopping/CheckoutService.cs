using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Checkout;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Shopping;

/// <summary>
/// FR-8: one checkout makes one order per shop inside one checkout group, all saved in one transaction.
/// Payable = items - coupon discount + delivery fees - store credit.
/// </summary>
public sealed class CheckoutService(
    AppDbContext db, CartService carts, Ledger ledger, Notifier notifier, ISettingsReader settings, IClock clock)
{
    private static readonly string[] OnlineMethods = ["bKash", "Nagad", "Card"];

    private sealed record ShopPlan(Shop Shop, List<LineState> Lines, decimal ItemsTotal, decimal Fee)
    {
        public decimal Discount { get; set; }
        public decimal Credit { get; set; }
        public decimal Total => ItemsTotal - Discount + Fee - Credit;
    }

    private sealed record Plan(
        List<ShopPlan> Shops, Address Address, bool InsideDhaka, Coupon? Coupon, decimal Discount, decimal CreditBalance, decimal CreditUsed,
        decimal Payable, decimal CodLimit, string? CouponMessage, List<LineState> Ordered)
    {
        public decimal ItemsTotal => Shops.Sum(s => s.ItemsTotal);
        public decimal DeliveryTotal => Shops.Sum(s => s.Fee);
    }

    public async Task<CheckoutSummaryDto> SummaryAsync(Guid userId, CheckoutRequest request, CancellationToken ct = default)
    {
        var plan = await PlanAsync(userId, request, strictCoupon: false, ct);
        return new CheckoutSummaryDto(
            plan.Shops.Select(s => new CheckoutShopDto(s.Shop.Id, s.Shop.Name, s.ItemsTotal, s.Fee, s.Discount)).ToList(),
            plan.ItemsTotal, plan.DeliveryTotal, plan.Discount, plan.CreditBalance, plan.CreditUsed, plan.Payable,
            plan.Payable > 0 && plan.Payable <= plan.CodLimit, plan.CodLimit, plan.CouponMessage);
    }

    public async Task<CheckoutResultDto> PlaceAsync(Guid userId, CheckoutRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 64)
        {
            throw new AppException(400, Messages.Invalid);
        }

        // FR-8.11: a second request with the same key returns the first result and creates nothing.
        if (await ExistingAsync(userId, idempotencyKey, ct) is { } repeated)
        {
            return repeated;
        }

        var buyer = await db.Users.FirstAsync(u => u.Id == userId, ct);
        if (!buyer.EmailConfirmed)
        {
            throw new AppException(403, Messages.VerifyEmailFirst); // BR-35
        }

        var plan = await PlanAsync(userId, request, strictCoupon: true, ct);
        var online = OnlineMethods.Contains(request.PaymentMethod) && plan.Payable > 0;
        if (request.PaymentMethod == "COD" && plan.Payable > plan.CodLimit)
        {
            throw new AppException(400, Messages.Invalid); // BR-11
        }

        var now = clock.UtcNow;
        var window = await settings.GetIntAsync("unpaidOrderCancelMinutes", 30, ct);
        var defaultRate = await settings.GetDecimalAsync("commissionPercent", 10, ct);
        var group = new CheckoutGroup
        {
            Id = Guid.CreateVersion7(),
            BuyerId = userId,
            IdempotencyKey = idempotencyKey,
            ItemsTotal = plan.ItemsTotal,
            DeliveryTotal = plan.DeliveryTotal,
            DiscountTotal = plan.Discount,
            CreditUsed = plan.CreditUsed,
            PayableTotal = plan.Payable,
            PaymentMethod = request.PaymentMethod,
            Status = online ? "AwaitingPayment" : "Placed",
            ExpiresAt = online ? now.AddMinutes(window) : null,
        };
        db.CheckoutGroups.Add(group);

        var lowStockLimit = await settings.GetIntAsync("lowStockLimit", 5, ct);
        var orders = new List<Order>();
        foreach (var shopPlan in plan.Shops)
        {
            var rate = shopPlan.Shop.CommissionPercent ?? defaultRate;
            var order = new Order
            {
                Id = Guid.CreateVersion7(),
                CheckoutGroupId = group.Id,
                BuyerId = userId,
                ShopId = shopPlan.Shop.Id,
                CouponId = shopPlan.Discount > 0 ? plan.Coupon?.Id : null,
                Status = online ? "AwaitingPayment" : "Pending",
                ItemsTotal = shopPlan.ItemsTotal,
                DeliveryFee = shopPlan.Fee,
                DiscountAmount = shopPlan.Discount,
                CreditUsed = shopPlan.Credit,
                TotalAmount = shopPlan.Total,
                CommissionPercent = rate,
                CommissionAmount = Math.Round(shopPlan.ItemsTotal * rate / 100, 2, MidpointRounding.AwayFromZero), // BR-45: before the coupon
                // DB-5: the order keeps its own copy of the address.
                ShipName = plan.Address.FullName,
                ShipPhone = plan.Address.Phone,
                ShipAddress = $"{plan.Address.AddressLine}, {plan.Address.Area.Name}, {plan.Address.District.Name}, {plan.Address.Division.Name}",
                ShipAreaId = plan.Address.AreaId,
            };
            foreach (var line in shopPlan.Lines)
            {
                ReserveStock(line, lowStockLimit, shopPlan.Shop);
                order.OrderItems.Add(new OrderItem
                {
                    Id = Guid.CreateVersion7(),
                    ProductId = line.Product!.Id,
                    VariantId = line.Variant?.Id,
                    ProductName = line.Product.Name,
                    VariantText = line.Variant?.OptionText,
                    Sku = line.Variant?.Sku,
                    UnitPrice = line.UnitPrice,
                    Quantity = line.Item.Quantity,
                });
            }

            order.OrderStatusHistories.Add(new OrderStatusHistory { OrderId = order.Id, ToStatus = order.Status, ChangedById = userId, Note = "Order placed" });
            db.Orders.Add(order);
            orders.Add(order);

            // FR-9.10: store credit is held at placement, against each order it pays for.
            if (shopPlan.Credit > 0)
            {
                await ledger.PostAsync(Ledger.EventId($"credit-hold:{order.Id}"), [
                    new(Ledger.StoreCredit, -shopPlan.Credit, "CreditUsed", UserId: userId, OrderId: order.Id, RefId: order.Id),
                    new(Ledger.Receivable, shopPlan.Credit, "CreditUsed", OrderId: order.Id, RefId: order.Id),
                ], ct);
            }
        }

        if (plan.Payable > 0)
        {
            db.Payments.Add(new Payment
            {
                Id = Guid.CreateVersion7(),
                CheckoutGroupId = group.Id,
                BuyerId = userId,
                Method = request.PaymentMethod,
                Amount = plan.Payable,
                Status = online ? "Initiated" : "Unpaid",
                TransactionId = online ? $"EM{Guid.NewGuid():N}"[..30] : null,
            });
        }

        if (plan.Coupon is not null && plan.Discount > 0)
        {
            plan.Coupon.UsedCount++; // FR-19.3: counted at placement, given back on cancel or expiry.
            db.CouponRedemptions.Add(new CouponRedemption { CouponId = plan.Coupon.Id, UserId = userId, CheckoutGroupId = group.Id, DiscountAmount = plan.Discount });
        }

        // FR-6.10: only the ordered lines leave the cart.
        db.CartItems.RemoveRange(plan.Ordered.Select(l => l.Item));
        var cart = await db.Carts.FirstAsync(c => c.UserId == userId, ct);
        cart.CouponId = null;

        // Order numbers come from a database sequence on insert (DB-6), so the messages are written after the first save,
        // inside the same transaction: the orders and their notifications are saved together or not at all (AD-9).
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // NFR-4: another buyer took the stock between our read and our write.
            throw new AppException(409, string.Format(Messages.NotEnoughStock, "an item in your cart", 0));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await ExistingAsync(userId, idempotencyKey, ct) ?? throw new AppException(409, Messages.Conflict); // same key raced in parallel
        }

        var numbers = string.Join(", ", orders.Select(o => o.OrderNumber));
        notifier.Notify(userId, "OrderPlaced", "Order placed", string.Format(Messages.OrderPlaced, numbers), "/orders");
        notifier.Email(buyer.Email, "Your EMarket order",
            $"{string.Format(Messages.OrderPlaced, numbers)} Payable: Tk {plan.Payable:N2} ({request.PaymentMethod})."
            + (online ? $" Please pay within {window} minutes." : ""));
        if (!online)
        {
            // FR-8.14: sellers hear about a COD order at once; for online payment only after the payment is confirmed.
            foreach (var order in orders)
            {
                var owner = plan.Shops.First(s => s.Shop.Id == order.ShopId).Shop.OwnerId;
                notifier.Notify(owner, "OrderPlaced", "New order", $"Order {order.OrderNumber} is waiting for you.", $"/seller/orders/{order.Id}");
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (await ExistingAsync(userId, idempotencyKey, ct, repeated: false))!;
    }

    private async Task<Plan> PlanAsync(Guid userId, CheckoutRequest request, bool strictCoupon, CancellationToken ct)
    {
        var cart = await carts.LoadAsync(userId, ct) ?? throw new AppException(409, Messages.Conflict);
        var states = await carts.StatesAsync(cart, userId, ct);

        // FR-8.9: a short line stops the whole checkout; lines that are not sold at all are left out (FR-6.7).
        if (states.FirstOrDefault(s => s.Product is not null && s.Problem is not null && s.Stock < s.Item.Quantity && s.Problem.StartsWith("Not enough")) is { } shortLine)
        {
            throw new AppException(409, string.Format(Messages.NotEnoughStock, shortLine.Product!.Name, shortLine.Stock));
        }

        var ordered = states.Where(s => s.Available).ToList();
        if (ordered.Count == 0)
        {
            throw new AppException(409, Messages.Conflict);
        }

        var address = await db.Addresses.Include(a => a.Area).Include(a => a.District).Include(a => a.Division)
            .FirstOrDefaultAsync(a => a.Id == request.AddressId && a.UserId == userId, ct) ?? throw new AppException(400, Messages.Required);
        var insideDhaka = address.Area.IsInsideDhaka || address.District.IsInsideDhaka;
        var defaultFee = await settings.GetDecimalAsync("deliveryFee", 60, ct);
        var freeAbove = await settings.GetDecimalAsync("freeDeliveryAbove", 2000, ct);

        var shops = ordered.GroupBy(l => l.Product!.ShopId).Select(g =>
        {
            var shop = g.First().Product!.Shop;
            var items = g.Sum(l => l.UnitPrice * l.Item.Quantity);
            return new ShopPlan(shop, g.ToList(), items, CartService.DeliveryFee(shop, items, insideDhaka, defaultFee, freeAbove));
        }).OrderBy(s => s.Shop.Name).ToList();

        // Coupon: one per checkout, shared between the orders by item totals (BR-16).
        Coupon? coupon = null;
        decimal discount = 0;
        string? couponMessage = null;
        var code = request.CouponCode ?? cart.Coupon?.Code;
        if (!string.IsNullOrWhiteSpace(code))
        {
            try
            {
                coupon = await carts.FindCouponAsync(code, ct);
                discount = CouponRules.TryDiscount(coupon, shops.Sum(s => s.ItemsTotal), await carts.UsesAsync(coupon.Id, userId, ct), clock.UtcNow)
                    ?? throw new AppException(400, Messages.BadCoupon);
                couponMessage = string.Format(Messages.CouponApplied, discount.ToString("N2"));
            }
            catch (AppException) when (!strictCoupon)
            {
                (coupon, discount, couponMessage) = (null, 0, Messages.BadCoupon);
            }
        }

        var shares = Ledger.Split(discount, shops.Select(s => s.ItemsTotal).ToList());
        for (var i = 0; i < shops.Count; i++)
        {
            shops[i].Discount = shares[i];
        }

        // Store credit pays up to the payable total and never goes below 0 (BR-17).
        var balance = await ledger.StoreCreditAsync(userId, ct);
        var beforeCredit = shops.Sum(s => s.Total);
        var creditUsed = request.UseStoreCredit ? Math.Max(0, Math.Min(balance, beforeCredit)) : 0;
        var creditShares = Ledger.Split(creditUsed, shops.Select(s => s.Total).ToList());
        for (var i = 0; i < shops.Count; i++)
        {
            shops[i].Credit = creditShares[i];
        }

        var codLimit = await settings.GetDecimalAsync("codMaxAmount", 20000, ct);
        return new Plan(shops, address, insideDhaka, coupon, discount, balance, creditUsed, beforeCredit - creditUsed, codLimit, couponMessage, ordered);
    }

    private void ReserveStock(LineState line, int lowStockLimit, Shop shop)
    {
        int before;
        if (line.Variant is { } variant)
        {
            before = variant.Stock;
            variant.Stock -= line.Item.Quantity;
        }
        else
        {
            before = line.Product!.Stock;
            line.Product.Stock -= line.Item.Quantity;
        }

        var after = before - line.Item.Quantity;
        if (after < 0)
        {
            throw new AppException(409, string.Format(Messages.NotEnoughStock, line.Product!.Name, before));
        }

        // FR-4.10: one alert when stock crosses the limit, not one per order.
        if (before > lowStockLimit && after <= lowStockLimit)
        {
            notifier.Notify(shop.OwnerId, "StockLow", "Stock is low", $"{line.Product!.Name} {line.Variant?.OptionText} has {after} left.", "/seller/products");
        }
    }

    private async Task<CheckoutResultDto?> ExistingAsync(Guid userId, string key, CancellationToken ct, bool repeated = true)
    {
        var group = await db.CheckoutGroups.AsNoTracking().Include(g => g.Orders).ThenInclude(o => o.Shop)
            .FirstOrDefaultAsync(g => g.IdempotencyKey == key, ct);
        if (group is null)
        {
            return null;
        }

        if (group.BuyerId != userId)
        {
            throw new AppException(409, Messages.Conflict);
        }

        return new CheckoutResultDto(group.Id,
            group.Orders.OrderBy(o => o.Shop.Name).Select(o => new PlacedOrderDto(o.Id, o.OrderNumber, o.Shop.Name, o.TotalAmount, o.Status)).ToList(),
            group.PayableTotal, group.PaymentMethod, group.Status, group.Status == "AwaitingPayment", group.ExpiresAt, repeated);
    }
}
