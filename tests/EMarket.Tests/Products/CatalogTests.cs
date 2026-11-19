using EMarket.BLL.Catalog;
using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Catalog;
using EMarket.Tests.TestUtils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Shouldly;

namespace EMarket.Tests.Products;

public sealed class CatalogTests
{
    private readonly AppDbContext db = TestDb.Create();
    private readonly FakeClock clock = new();
    private readonly CategoryService categories;
    private readonly ProductService products;
    private readonly CatalogQueryService catalog;
    private readonly User admin;
    private readonly User seller;
    private readonly Shop shop;
    private readonly Category phones;

    public CatalogTests()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var notifier = new Notifier(db, clock);
        categories = new CategoryService(db, cache, notifier);
        products = new ProductService(db, categories, new FakeFiles(), notifier);
        catalog = new CatalogQueryService(db, categories, cache, clock);
        admin = db.AddUser(Roles.Admin);
        (seller, shop) = AddSeller("Rahim Store");
        var electronics = new Category { Id = Guid.NewGuid(), Name = "Electronics", Slug = "electronics", IsActive = true };
        phones = new Category { Id = Guid.NewGuid(), Name = "Mobile phones", Slug = "mobile-phones", ParentId = electronics.Id, IsActive = true };
        db.Categories.AddRange(electronics, phones);
        db.SaveChanges();
    }

    private (User, Shop) AddSeller(string name)
    {
        var user = db.AddUser(Roles.Seller);
        var s = new Shop
        {
            Id = Guid.NewGuid(), OwnerId = user.Id, Name = name, Slug = name.ToLower().Replace(' ', '-'), Description = "",
            Status = "Active", PayoutMethod = "bKash", PayoutAccount = "enc",
        };
        db.Shops.Add(s);
        db.SaveChanges();
        return (user, s);
    }

    private SaveProductRequest Request(string name = "Samsung Galaxy A15", decimal price = 20000, params VariantInput[] variants) => new()
    {
        Name = name, CategoryId = phones.Id, Brand = name.Split(' ')[0], Description = "A good phone with a big battery.", Price = price,
        Stock = 10, Variants = [.. variants],
    };

    private static UploadFile Photo() => new(new MemoryStream([0xFF, 0xD8, 0xFF]), 3);

    private async Task<Guid> LiveProductAsync(string name = "Samsung Galaxy A15", decimal price = 20000)
    {
        var id = await products.CreateAsync(seller.Id, Request(name, price));
        await products.AddImageAsync(seller.Id, id, Photo());
        await products.SubmitAsync(seller.Id, id);
        await products.ApproveAsync(admin.Id, id);
        return id;
    }

    [Fact]
    public async Task A_product_is_visible_only_after_approval()
    {
        var id = await products.CreateAsync(seller.Id, Request());
        (await db.Products.FindAsync(id))!.Status.ShouldBe("Draft");

        await Should.ThrowAsync<AppException>(() => products.SubmitAsync(seller.Id, id)); // no image yet
        await products.AddImageAsync(seller.Id, id, Photo());
        await products.SubmitAsync(seller.Id, id);
        (await catalog.SearchAsync(new ProductFilter())).TotalCount.ShouldBe(0);

        await products.ApproveAsync(admin.Id, id);
        (await catalog.SearchAsync(new ProductFilter())).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Editing_the_description_needs_approval_again_but_stock_does_not()
    {
        var id = await LiveProductAsync();

        var stockOnly = Request();
        stockOnly.Stock = 3;
        await products.UpdateAsync(seller.Id, id, stockOnly);
        (await db.Products.FindAsync(id))!.Status.ShouldBe("Active");

        var newText = Request();
        newText.Description = "A good phone with a big battery and a new camera.";
        await products.UpdateAsync(seller.Id, id, newText);
        (await db.Products.FindAsync(id))!.Status.ShouldBe("Pending");
    }

    [Fact]
    public async Task Discount_price_must_be_lower_than_price()
    {
        var request = Request();
        request.DiscountPrice = 20000;

        await Should.ThrowAsync<AppException>(() => products.CreateAsync(seller.Id, request));
    }

    [Fact]
    public async Task Sku_is_unique_within_the_shop_across_products()
    {
        await products.CreateAsync(seller.Id, Request("Phone one", 100, new VariantInput { Colour = "Red", Sku = "PH-RED", Stock = 1 }));

        await Should.ThrowAsync<AppException>(() =>
            products.CreateAsync(seller.Id, Request("Phone two", 100, new VariantInput { Colour = "Red", Sku = "ph-red", Stock = 1 })));
    }

    [Fact]
    public async Task Another_seller_gets_403_for_a_product()
    {
        var id = await products.CreateAsync(seller.Id, Request());
        var (other, _) = AddSeller("Nila Mart");

        (await Should.ThrowAsync<AppException>(() => products.GetOwnAsync(other.Id, id))).StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task Admin_hidden_product_cannot_be_unhidden_by_the_seller()
    {
        var id = await LiveProductAsync();
        await products.AdminSetHiddenAsync(admin.Id, id, true, "Copied photos from another shop");

        await Should.ThrowAsync<AppException>(() => products.SetHiddenAsync(seller.Id, id, false));
    }

    [Fact]
    public async Task Suspended_shop_products_disappear_from_search()
    {
        await LiveProductAsync();
        shop.Status = "Suspended";
        await db.SaveChangesAsync();

        (await catalog.SearchAsync(new ProductFilter())).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Search_is_partial_and_ignores_case_and_sorting_by_price_works()
    {
        await LiveProductAsync("Samsung Galaxy A15", 20000);
        await LiveProductAsync("Samsung Galaxy S24", 90000);
        await LiveProductAsync("Walton Primo", 9000);

        var found = await catalog.SearchAsync(new ProductFilter { Q = "SAMSU", Sort = "price-asc" });

        found.Items.Select(p => p.Name).ShouldBe(["Samsung Galaxy A15", "Samsung Galaxy S24"]);
    }

    [Fact]
    public async Task Forty_five_results_make_three_pages()
    {
        for (var i = 0; i < 45; i++)
        {
            db.Products.Add(new Product
            {
                Id = Guid.NewGuid(), ShopId = shop.Id, CategoryId = phones.Id, Name = $"Item {i}", Description = "d", Price = 100, Stock = 1, Status = "Active",
            });
        }

        await db.SaveChangesAsync();

        var last = await catalog.SearchAsync(new ProductFilter { Page = 3 });

        (last.TotalPages, last.Items.Count).ShouldBe((3, 5));
    }

    [Fact]
    public async Task A_third_category_level_is_refused_and_a_used_category_cannot_be_deleted()
    {
        await Should.ThrowAsync<AppException>(() =>
            categories.CreateAsync(admin.Id, new SaveCategoryRequest { Name = "Android", ParentId = phones.Id }));

        await products.CreateAsync(seller.Id, Request());
        (await Should.ThrowAsync<AppException>(() => categories.DeleteAsync(admin.Id, phones.Id))).StatusCode.ShouldBe(409);
    }

    [Fact]
    public async Task Rejection_reason_reaches_the_seller()
    {
        var id = await products.CreateAsync(seller.Id, Request());
        await products.AddImageAsync(seller.Id, id, Photo());
        await products.SubmitAsync(seller.Id, id);

        await products.RejectAsync(admin.Id, id, "Please add a clear front photo");

        (await products.GetOwnAsync(seller.Id, id)).RejectionReason.ShouldBe("Please add a clear front photo");
        (await db.Notifications.AnyAsync(n => n.UserId == seller.Id && n.Type == "ProductRejected")).ShouldBeTrue();
    }
}
