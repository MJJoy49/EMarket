using EMarket.BLL.Common;
using EMarket.BLL.Finance;
using EMarket.BLL.Shopping;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Cart;
using EMarket.Shared.Dtos.Checkout;
using EMarket.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shouldly;

namespace EMarket.Tests.Checkout;

public sealed class CheckoutTests
{
    private readonly AppDbContext db = TestDb.Create();
    private readonly FakeClock clock = new();
    private readonly CartService carts;
    private readonly CheckoutService checkout;
    private readonly Ledger ledger;
    private readonly User buyer;
    private readonly Guid addressId;
    private readonly Category category;

    public CheckoutTests()
    {
        var settings = new SettingsReader(db, new MemoryCache(new MemoryCacheOptions()));
        ledger = new Ledger(db);
        carts = new CartService(db, settings, clock);
        checkout = new CheckoutService(db, carts, ledger, new Notifier(db, clock), settings, clock);
        buyer = db.AddUser();
        category = new Category { Id = Guid.NewGuid(), Name = "Phones", Slug = "phones", IsActive = true };
        db.Categories.Add(category);
        addressId = AddAddress(buyer.Id, insideDhaka: true);
        db.SaveChanges();
    }

    private Guid AddAddress(Guid userId, bool insideDhaka)
    {
        var division = new Location { Id = Guid.NewGuid(), Level = "Division", Name = "Dhaka" };
        var district = new Location { Id = Guid.NewGuid(), Level = "District", Name = insideDhaka ? "Dhaka" : "Gazipur", ParentId = division.Id, IsInsideDhaka = insideDhaka };
        var area = new Location { Id = Guid.NewGuid(), Level = "Area", Name = "Dhanmondi", ParentId = district.Id, IsInsideDhaka = insideDhaka };
        var address = new Address
        {
            Id = Guid.NewGuid(), UserId = userId, FullName = "Karim", Phone = "01712345678", DivisionId = division.Id, DistrictId = district.Id,
            AreaId = area.Id, AddressLine = "House 1, Road 2", IsDefault = true,
        };
        db.Locations.AddRange(division, district, area);
        db.Addresses.Add(address);
        return address.Id;
    }

    private Product AddProduct(string shopName, decimal price, int stock, decimal? insideFee = 60, User? owner = null)
    {
        var seller = owner ?? db.AddUser(Roles.Seller);
        var shop = db.Shops.Local.FirstOrDefault(s => s.Name == shopName) ?? new Shop
        {
            Id = Guid.NewGuid(), OwnerId = seller.Id, Name = shopName, Slug = shopName.ToLower(), Description = "", Status = "Active",
            InsideDhakaFee = insideFee, OutsideDhakaFee = 120, FreeDeliveryAbove = 2000, PayoutMethod = "bKash", PayoutAccount = "enc",
        };
        if (db.Entry(shop).State == EntityState.Detached)
        {
            db.Shops.Add(shop);
        }

        var product = new Product
        {
            Id = Guid.NewGuid(), Shop = shop, ShopId = shop.Id, CategoryId = category.Id, Name = $"{shopName} item {price}", Description = "d",
            Price = price, Stock = stock, Status = "Active",
        };
        db.Products.Add(product);
        db.SaveChanges();
        return product;
    }

    private Task Add(Product product, int quantity = 1, Guid? userId = null) =>
        carts.AddAsync(userId ?? buyer.Id, new AddToCartRequest { ProductId = product.Id, Quantity = quantity });

    private CheckoutRequest Request(string method = "COD", bool credit = false, string? coupon = null) =>
        new() { AddressId = addressId, PaymentMethod = method, UseStoreCredit = credit, CouponCode = coupon };

    [Fact]
    public async Task Two_shops_make_one_group_two_orders_and_reserve_stock()
    {
        var a = AddProduct("Rahim", 500, stock: 5);
        var b = AddProduct("Nila", 700, stock: 5);
        await Add(a, 2);
        await Add(b);

        var result = await checkout.PlaceAsync(buyer.Id, Request(), "key-1");

        result.Orders.Count.ShouldBe(2);
        result.Orders.ShouldAllBe(o => o.Status == "Pending");
        result.Payable.ShouldBe(1000 + 700 + 60 + 60);
        (await db.CheckoutGroups.CountAsync()).ShouldBe(1);
        (await db.Products.FindAsync(a.Id))!.Stock.ShouldBe(3);
        (await db.CartItems.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task The_same_key_twice_creates_one_checkout_group()
    {
        await Add(AddProduct("Rahim", 500, stock: 5));
        var first = await checkout.PlaceAsync(buyer.Id, Request(), "key-same");

        var second = await checkout.PlaceAsync(buyer.Id, Request(), "key-same");

        second.Repeated.ShouldBeTrue();
        second.CheckoutGroupId.ShouldBe(first.CheckoutGroupId);
        (await db.CheckoutGroups.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Delivery_fee_follows_the_shop_and_the_free_limit()
    {
        await Add(AddProduct("Rahim", 2500, stock: 5)); // above the Tk 2,000 free-delivery limit
        await Add(AddProduct("Nila", 100, stock: 5, insideFee: 120));

        var summary = await checkout.SummaryAsync(buyer.Id, Request());

        summary.DeliveryTotal.ShouldBe(120);
    }

    [Fact]
    public async Task A_seller_cannot_buy_from_the_own_shop()
    {
        var seller = db.AddUser(Roles.Seller);
        var product = AddProduct("Own", 500, stock: 5, owner: seller);

        (await Should.ThrowAsync<AppException>(() => Add(product, userId: seller.Id))).Message.ShouldBe(Messages.OwnProduct);
    }

    [Fact]
    public async Task Quantity_above_ten_or_above_stock_is_refused()
    {
        var product = AddProduct("Rahim", 100, stock: 3);

        await Should.ThrowAsync<AppException>(() => Add(product, 4));
        await Should.ThrowAsync<AppException>(() => Add(AddProduct("Nila", 100, stock: 50), 11));
        (await db.CartItems.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Coupon_is_split_by_item_totals_and_counted_once()
    {
        db.Coupons.Add(new Coupon
        {
            Id = Guid.NewGuid(), Code = "SAVE100", DiscountType = "Fixed", DiscountValue = 100, StartsAt = clock.UtcNow.AddDays(-1),
            ExpiresAt = clock.UtcNow.AddDays(1), PerUserLimit = 1, IsActive = true,
        });
        await db.SaveChangesAsync();
        await Add(AddProduct("Rahim", 300, stock: 5));
        await Add(AddProduct("Nila", 100, stock: 5));

        var result = await checkout.PlaceAsync(buyer.Id, Request(coupon: "save100"), "key-c");

        var orders = await db.Orders.ToListAsync();
        orders.Sum(o => o.DiscountAmount).ShouldBe(100);
        orders.Single(o => o.ItemsTotal == 300).DiscountAmount.ShouldBe(75);
        result.Payable.ShouldBe(400 - 100 + 120);
        (await db.Coupons.SingleAsync()).UsedCount.ShouldBe(1);
        await Add(AddProduct("Third", 300, stock: 5));
        await Should.ThrowAsync<AppException>(() => checkout.PlaceAsync(buyer.Id, Request(coupon: "SAVE100"), "key-d")); // per-user limit
    }

    [Fact]
    public async Task Store_credit_pays_part_and_the_ledger_balances()
    {
        await ledger.PostAsync(Guid.NewGuid(), [
            new(Ledger.StoreCredit, 300, "Refund", UserId: buyer.Id),
            new(Ledger.PlatformCash, -300, "Refund"),
        ]);
        await db.SaveChangesAsync();
        await Add(AddProduct("Rahim", 940, stock: 5));

        var result = await checkout.PlaceAsync(buyer.Id, Request(credit: true), "key-credit");

        result.Payable.ShouldBe(1000 - 300);
        (await ledger.StoreCreditAsync(buyer.Id)).ShouldBe(0);
        (await db.LedgerEntries.SumAsync(e => e.Amount)).ShouldBe(0);
    }

    [Fact]
    public async Task Cash_on_delivery_is_refused_above_the_limit()
    {
        await Add(AddProduct("Rahim", 9000, stock: 5), 3); // Tk 27,000 > Tk 20,000

        (await checkout.SummaryAsync(buyer.Id, Request())).CodAllowed.ShouldBeFalse();
        await Should.ThrowAsync<AppException>(() => checkout.PlaceAsync(buyer.Id, Request(), "key-cod"));
    }

    [Fact]
    public async Task Online_payment_waits_and_sellers_are_not_told_yet()
    {
        var product = AddProduct("Rahim", 500, stock: 5);
        await Add(product);

        var result = await checkout.PlaceAsync(buyer.Id, Request("bKash"), "key-online");

        result.AwaitingPayment.ShouldBeTrue();
        result.PayBefore.ShouldBe(clock.UtcNow.AddMinutes(30));
        (await db.Payments.SingleAsync()).Status.ShouldBe("Initiated");
        var sellerId = (await db.Shops.SingleAsync()).OwnerId;
        (await db.Notifications.AnyAsync(n => n.UserId == sellerId)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_unverified_buyer_cannot_place_an_order()
    {
        var unverified = db.AddUser(emailConfirmed: false);
        await Add(AddProduct("Rahim", 500, stock: 5), userId: unverified.Id);
        var request = new CheckoutRequest { AddressId = AddAddress(unverified.Id, true), PaymentMethod = "COD" };
        await db.SaveChangesAsync();

        (await Should.ThrowAsync<AppException>(() => checkout.PlaceAsync(unverified.Id, request, "key-u"))).Message.ShouldBe(Messages.VerifyEmailFirst);
    }

    [Fact]
    public async Task An_unavailable_line_stays_in_the_cart_after_checkout()
    {
        var gone = AddProduct("Rahim", 500, stock: 5);
        await Add(gone);
        await Add(AddProduct("Nila", 300, stock: 5));
        gone.Status = "Hidden";
        await db.SaveChangesAsync();

        var result = await checkout.PlaceAsync(buyer.Id, Request(), "key-h");

        result.Orders.Count.ShouldBe(1);
        (await db.CartItems.SingleAsync()).ProductId.ShouldBe(gone.Id);
    }

    [Fact]
    public void Split_parts_always_add_up() =>
        Ledger.Split(100, [1, 1, 1]).Sum().ShouldBe(100);
}
