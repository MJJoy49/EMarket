using EMarket.BLL.AfterSale;
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
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace EMarket.Tests.TestUtils;

/// <summary>A small marketplace with one shop, one buyer and every service wired together, for flow tests.</summary>
public sealed class Market
{
    public AppDbContext Db { get; } = TestDb.Create();
    public FakeClock Clock { get; } = new();
    public Ledger Ledger { get; }
    public CartService Carts { get; }
    public CheckoutService Checkout { get; }
    public OrderService Orders { get; }
    public PaymentService Payments { get; }
    public RefundService Refunds { get; }
    public ReturnService Returns { get; }
    public ReviewService Reviews { get; }
    public EarningsService Earnings { get; }
    public User Buyer { get; }
    public User Seller { get; }
    public User Admin { get; }
    public Shop Shop { get; }
    public Guid AddressId { get; }
    public PasswordHasher<User> Hasher { get; } = new();

    public Market()
    {
        var settings = new SettingsReader(Db, new MemoryCache(new MemoryCacheOptions()));
        var notifier = new Notifier(Db, Clock);
        var gateway = new FakeGateway(new GatewayOptions());
        Ledger = new Ledger(Db);
        Carts = new CartService(Db, settings, Clock);
        Checkout = new CheckoutService(Db, Carts, Ledger, notifier, settings, Clock);
        Refunds = new RefundService(Db, gateway, Ledger, notifier, NullLogger<RefundService>.Instance);
        Orders = new OrderService(Db, Ledger, notifier, settings, Clock, Refunds);
        Payments = new PaymentService(Db, gateway, Orders, Ledger, notifier, Clock, NullLogger<PaymentService>.Instance);
        Returns = new ReturnService(Db, new FakeFiles(), Ledger, notifier, settings, Clock, Refunds);
        Reviews = new ReviewService(Db, notifier, Clock);
        Earnings = new EarningsService(Db, Ledger, notifier, settings, new FakeSecrets(), Hasher, new FakeFiles(), Clock);

        Buyer = Db.AddUser();
        Seller = Db.AddUser(Roles.Seller);
        Seller.PasswordHash = Hasher.HashPassword(Seller, "Tiger7Lamp");
        Admin = Db.AddUser(Roles.Admin);
        Shop = new Shop
        {
            Id = Guid.NewGuid(), OwnerId = Seller.Id, Name = "Rahim", Slug = "rahim", Description = "", Status = "Active",
            InsideDhakaFee = 60, OutsideDhakaFee = 120, FreeDeliveryAbove = 2000, PayoutMethod = "bKash", PayoutAccount = "enc:01712345678",
        };
        var division = new Location { Id = Guid.NewGuid(), Level = "Division", Name = "Dhaka" };
        var district = new Location { Id = Guid.NewGuid(), Level = "District", Name = "Dhaka", ParentId = division.Id, IsInsideDhaka = true };
        var area = new Location { Id = Guid.NewGuid(), Level = "Area", Name = "Dhanmondi", ParentId = district.Id, IsInsideDhaka = true };
        AddressId = Guid.NewGuid();
        Db.AddRange(Shop, division, district, area, new Category { Id = CategoryId, Name = "Phones", Slug = "phones", IsActive = true });
        Db.Addresses.Add(new Address
        {
            Id = AddressId, UserId = Buyer.Id, FullName = "Karim", Phone = "01712345678", DivisionId = division.Id, DistrictId = district.Id,
            AreaId = area.Id, AddressLine = "House 1, Road 2", IsDefault = true,
        });
        Db.SaveChanges();
    }

    public Guid CategoryId { get; } = Guid.NewGuid();

    public Product AddProduct(decimal price, int stock = 10)
    {
        var product = new Product { Id = Guid.NewGuid(), ShopId = Shop.Id, CategoryId = CategoryId, Name = $"Item {price}", Description = "d", Price = price, Stock = stock, Status = "Active" };
        Db.Products.Add(product);
        Db.SaveChanges();
        return product;
    }

    public async Task<Order> PlaceAsync(IEnumerable<(Product Product, int Quantity)> lines, string? coupon = null, string method = "COD")
    {
        foreach (var (product, quantity) in lines)
        {
            await Carts.AddAsync(Buyer.Id, new AddToCartRequest { ProductId = product.Id, Quantity = quantity });
        }

        var result = await Checkout.PlaceAsync(Buyer.Id, new CheckoutRequest { AddressId = AddressId, PaymentMethod = method, CouponCode = coupon }, Guid.NewGuid().ToString());
        return await Db.Orders.Include(o => o.OrderItems).SingleAsync(o => o.Id == result.Orders.Single().Id);
    }

    public async Task DeliverAsync(Order order)
    {
        await Orders.SellerMoveAsync(Seller.Id, order.Id, OrderRules.Confirmed, null);
        await Orders.SellerMoveAsync(Seller.Id, order.Id, OrderRules.Processing, null);
        await Orders.ShipAsync(Seller.Id, order.Id, new ShipRequest());
        await Orders.SellerMoveAsync(Seller.Id, order.Id, OrderRules.Delivered, null);
    }

    public async Task<decimal> LedgerTotalAsync() => await Db.LedgerEntries.SumAsync(e => e.Amount);
}
