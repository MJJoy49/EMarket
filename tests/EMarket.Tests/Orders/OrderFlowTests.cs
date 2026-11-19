using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.BLL.Orders;
using EMarket.BLL.Payments;
using EMarket.BLL.Shopping;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Cart;
using EMarket.Shared.Dtos.Checkout;
using EMarket.Shared.Dtos.Orders;
using EMarket.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace EMarket.Tests.Orders;

public sealed class OrderFlowTests
{
    private readonly AppDbContext db = TestDb.Create();
    private readonly FakeClock clock = new();
    private readonly Ledger ledger;
    private readonly CartService carts;
    private readonly CheckoutService checkout;
    private readonly OrderService orders;
    private readonly PaymentService payments;
    private readonly RefundService refunds;
    private readonly FakeGateway gateway = new(new GatewayOptions());
    private readonly User buyer;
    private readonly User seller;
    private readonly Guid addressId;
    private readonly Product product;

    public OrderFlowTests()
    {
        var settings = new SettingsReader(db, new MemoryCache(new MemoryCacheOptions()));
        var notifier = new Notifier(db, clock);
        ledger = new Ledger(db);
        carts = new CartService(db, settings, clock);
        checkout = new CheckoutService(db, carts, ledger, notifier, settings, clock);
        orders = new OrderService(db, ledger, notifier, settings, clock);
        payments = new PaymentService(db, gateway, orders, ledger, notifier, clock, NullLogger<PaymentService>.Instance);
        refunds = new RefundService(db, gateway, ledger, notifier, NullLogger<RefundService>.Instance);

        buyer = db.AddUser();
        seller = db.AddUser(Roles.Seller);
        var shop = new Shop
        {
            Id = Guid.NewGuid(), OwnerId = seller.Id, Name = "Rahim", Slug = "rahim", Description = "", Status = "Active",
            InsideDhakaFee = 60, OutsideDhakaFee = 120, FreeDeliveryAbove = 2000, PayoutMethod = "bKash", PayoutAccount = "enc",
        };
        var category = new Category { Id = Guid.NewGuid(), Name = "Phones", Slug = "phones", IsActive = true };
        product = new Product { Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = category.Id, Name = "Phone", Description = "d", Price = 1000, Stock = 5, Status = "Active" };
        var division = new Location { Id = Guid.NewGuid(), Level = "Division", Name = "Dhaka" };
        var district = new Location { Id = Guid.NewGuid(), Level = "District", Name = "Dhaka", ParentId = division.Id, IsInsideDhaka = true };
        var area = new Location { Id = Guid.NewGuid(), Level = "Area", Name = "Dhanmondi", ParentId = district.Id, IsInsideDhaka = true };
        addressId = Guid.NewGuid();
        db.AddRange(shop, category, product, division, district, area);
        db.Addresses.Add(new Address
        {
            Id = addressId, UserId = buyer.Id, FullName = "Karim", Phone = "01712345678", DivisionId = division.Id, DistrictId = district.Id,
            AreaId = area.Id, AddressLine = "House 1, Road 2", IsDefault = true,
        });
        db.SaveChanges();
    }

    private async Task<CheckoutResultDto> PlaceAsync(string method = "COD", int quantity = 1)
    {
        await carts.AddAsync(buyer.Id, new AddToCartRequest { ProductId = product.Id, Quantity = quantity });
        return await checkout.PlaceAsync(buyer.Id, new CheckoutRequest { AddressId = addressId, PaymentMethod = method }, Guid.NewGuid().ToString());
    }

    private async Task<int> StockAsync() => (await db.Products.AsNoTracking().SingleAsync(p => p.Id == product.Id)).Stock;

    private async Task<decimal> LedgerTotalAsync() => await db.LedgerEntries.SumAsync(e => e.Amount);

    [Fact]
    public async Task Buyer_can_cancel_before_processing_and_the_stock_comes_back()
    {
        var order = (await PlaceAsync(quantity: 2)).Orders.Single();
        (await StockAsync()).ShouldBe(3);

        await orders.CancelByBuyerAsync(buyer.Id, order.Id, "Changed my mind");

        (await StockAsync()).ShouldBe(5);
        (await db.Orders.SingleAsync()).Status.ShouldBe(OrderRules.Cancelled);
    }

    [Fact]
    public async Task Buyer_cannot_cancel_after_processing_starts()
    {
        var order = (await PlaceAsync()).Orders.Single();
        await orders.SellerMoveAsync(seller.Id, order.Id, OrderRules.Confirmed, null);
        await orders.SellerMoveAsync(seller.Id, order.Id, OrderRules.Processing, null);

        (await Should.ThrowAsync<AppException>(() => orders.CancelByBuyerAsync(buyer.Id, order.Id, "Too late now"))).Message.ShouldBe(Messages.CannotCancel);
    }

    [Fact]
    public async Task Seller_moves_step_by_step_and_delivery_books_the_earning()
    {
        var order = (await PlaceAsync()).Orders.Single();

        await Should.ThrowAsync<AppException>(() => orders.ShipAsync(seller.Id, order.Id, new ShipRequest())); // Pending -> Shipped skips steps
        await orders.SellerMoveAsync(seller.Id, order.Id, OrderRules.Confirmed, null);
        await orders.SellerMoveAsync(seller.Id, order.Id, OrderRules.Processing, null);
        await orders.ShipAsync(seller.Id, order.Id, new ShipRequest { CourierName = "Sundarban", TrackingNote = "CN-123" });
        await orders.SellerMoveAsync(seller.Id, order.Id, OrderRules.Delivered, null);

        var balances = await ledger.ShopBalancesAsync((await db.Shops.SingleAsync()).Id);
        balances[Ledger.SellerPending].ShouldBe(1000 - 100 + 60); // FR-15.2: item - 10% commission + fee
        (await db.Payments.SingleAsync()).Status.ShouldBe("Paid"); // COD cash recorded on delivery
        (await LedgerTotalAsync()).ShouldBe(0);
        (await db.OrderStatusHistories.CountAsync(h => h.OrderId == order.Id)).ShouldBe(5);
    }

    [Fact]
    public async Task Seller_cannot_see_an_order_that_waits_for_payment()
    {
        var order = (await PlaceAsync("bKash")).Orders.Single();

        (await Should.ThrowAsync<AppException>(() => orders.SellerDetailAsync(seller.Id, order.Id))).StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task A_verified_payment_moves_the_orders_to_pending()
    {
        var placed = await PlaceAsync("bKash");
        var start = await payments.StartAsync(buyer.Id, placed.CheckoutGroupId);

        FakeGateway.Decide(start.TransactionId, "VALID");
        (await payments.SettleAsync(start.TransactionId)).ShouldBe("Paid");
        (await payments.SettleAsync(start.TransactionId)).ShouldBe("Paid"); // the same callback again changes nothing

        (await db.Orders.SingleAsync()).Status.ShouldBe(OrderRules.Pending);
        (await db.LedgerEntries.CountAsync(e => e.Kind == "OrderReceived")).ShouldBe(2);
        (await LedgerTotalAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_callback_is_not_trusted_until_the_gateway_confirms_it()
    {
        var placed = await PlaceAsync("bKash");
        var start = await payments.StartAsync(buyer.Id, placed.CheckoutGroupId);

        (await payments.SettleAsync(start.TransactionId)).ShouldBe("Initiated"); // a forged success: the gateway still says unpaid

        (await db.Orders.SingleAsync()).Status.ShouldBe(OrderRules.AwaitingPayment);
    }

    [Fact]
    public async Task An_unpaid_order_expires_and_returns_the_stock()
    {
        var placed = await PlaceAsync("Card", quantity: 2);
        await payments.StartAsync(buyer.Id, placed.CheckoutGroupId);

        clock.UtcNow = clock.UtcNow.AddMinutes(31);
        (await payments.ExpireUnpaidAsync()).ShouldBe(1);

        (await db.Orders.SingleAsync()).Status.ShouldBe(OrderRules.Cancelled);
        (await StockAsync()).ShouldBe(5);
        (await db.Payments.SingleAsync()).Status.ShouldBe("Expired");
    }

    [Fact]
    public async Task Cancelling_a_paid_order_refunds_it_once()
    {
        var placed = await PlaceAsync("Nagad");
        var start = await payments.StartAsync(buyer.Id, placed.CheckoutGroupId);
        FakeGateway.Decide(start.TransactionId, "VALID");
        await payments.SettleAsync(start.TransactionId);

        await orders.CancelByBuyerAsync(buyer.Id, placed.Orders.Single().Id, "Found it cheaper");
        await refunds.ProcessPendingAsync();
        await refunds.ProcessPendingAsync();

        var refund = await db.Refunds.SingleAsync();
        (refund.Status, refund.Amount).ShouldBe(("Completed", 1060m));
        (await db.Payments.SingleAsync()).Status.ShouldBe("Refunded");
        (await LedgerTotalAsync()).ShouldBe(0);
    }

    [Fact]
    public void Only_the_listed_moves_are_allowed()
    {
        OrderRules.CanMove(OrderRules.Delivered, OrderRules.Pending, Actor.Admin).ShouldBeFalse();
        OrderRules.CanMove(OrderRules.Shipped, OrderRules.Cancelled, Actor.Admin).ShouldBeFalse();
        OrderRules.CanMove(OrderRules.Processing, OrderRules.Cancelled, Actor.Buyer).ShouldBeFalse();
        OrderRules.CanMove(OrderRules.Confirmed, OrderRules.Cancelled, Actor.Buyer).ShouldBeTrue();
    }
}
