using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Catalog;
using EMarket.Shared.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EMarket.BLL.Catalog;

/// <summary>Public browsing (FR-5). Only Active products of an Active shop in an active category are ever shown.</summary>
public sealed class CatalogQueryService(AppDbContext db, CategoryService categories, IMemoryCache cache, IClock clock)
{
    public const int PageSize = 20;

    public IQueryable<Product> Visible() => db.Products.AsNoTracking().Where(p =>
        p.Status == "Active" && p.Shop.Status == "Active" && p.Category.IsActive && (p.Category.Parent == null || p.Category.Parent.IsActive));

    public async Task<PagedResult<ProductListItemDto>> SearchAsync(ProductFilter filter, CancellationToken ct = default)
    {
        var query = Visible();
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            var term = filter.Q.Trim().ToLower();
            if (term.Length < 2)
            {
                throw new AppException(400, Messages.Invalid);
            }

            query = query.Where(p => p.Name.ToLower().Contains(term) || (p.Brand != null && p.Brand.ToLower().Contains(term))
                || p.Description.ToLower().Contains(term));
        }

        if (filter.CategoryId is { } categoryId)
        {
            query = query.Where(p => p.CategoryId == categoryId || p.Category.ParentId == categoryId);
        }

        if (filter.ShopId is { } shopId)
        {
            query = query.Where(p => p.ShopId == shopId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Brand))
        {
            var brand = filter.Brand.Trim().ToLower();
            query = query.Where(p => p.Brand != null && p.Brand.ToLower() == brand);
        }

        // Price filters use the price after discount, as shown on the card.
        if (filter.MinPrice is { } min)
        {
            query = query.Where(p => p.Price * (100 - p.DiscountPercent) / 100 >= min);
        }

        if (filter.MaxPrice is { } max)
        {
            query = query.Where(p => p.Price * (100 - p.DiscountPercent) / 100 <= max);
        }

        if (filter.MinRating is { } rating)
        {
            query = query.Where(p => p.AverageRating >= rating);
        }

        if (filter.InStockOnly)
        {
            query = query.Where(p => p.ProductVariants.Any(v => v.IsActive && v.Stock > 0) || (!p.ProductVariants.Any(v => v.IsActive) && p.Stock > 0));
        }

        query = filter.Sort switch
        {
            "price-asc" => query.OrderBy(p => p.Price * (100 - p.DiscountPercent) / 100).ThenBy(p => p.Id),
            "price-desc" => query.OrderByDescending(p => p.Price * (100 - p.DiscountPercent) / 100).ThenBy(p => p.Id),
            "top-rated" => query.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount).ThenBy(p => p.Id),
            _ => query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
        };

        var page = Math.Max(1, filter.Page);
        var total = await query.CountAsync(ct);
        var items = await Cards(query.Skip((page - 1) * PageSize).Take(PageSize)).ToListAsync(ct);
        return new PagedResult<ProductListItemDto>(items, page, PageSize, total);
    }

    public async Task<ProductDetailDto> DetailsAsync(Guid productId, CancellationToken ct = default)
    {
        var p = await Visible().Include(x => x.ProductImages).Include(x => x.ProductVariants).Include(x => x.Category).Include(x => x.Shop)
            .FirstOrDefaultAsync(x => x.Id == productId, ct) ?? throw new AppException(404, Messages.NotFound);
        var variants = p.ProductVariants.Where(v => v.IsActive).OrderBy(v => v.OptionText)
            .Select(v => new VariantDto(v.Id, v.Sku, v.OptionText, v.PriceOverride ?? p.Price, Pricing.Final(v.PriceOverride ?? p.Price, p.DiscountPercent), v.Stock, true))
            .ToList();
        var stock = variants.Count > 0 ? variants.Sum(v => v.Stock) : p.Stock;
        var images = p.ProductImages.OrderBy(i => i.SortOrder)
            .Select(i => new ProductImageDto(i.Id, Pricing.MediaUrl(i.Path), Pricing.MediaUrl(i.ThumbPath), i.SortOrder)).ToList();
        return new ProductDetailDto(p.Id, p.Name, p.Brand, p.Description, p.Price, Pricing.Final(p.Price, p.DiscountPercent), p.DiscountPercent,
            stock, stock > 0, p.AverageRating, p.ReviewCount, p.CategoryId, p.Category.Name, p.ShopId, p.Shop.Name, p.Shop.Slug, images, variants);
    }

    /// <summary>FR-5.9: a suspended or deleted shop shows "Shop unavailable" and no products.</summary>
    public async Task<ShopPageDto> ShopAsync(string slug, CancellationToken ct = default)
    {
        var shop = await db.Shops.AsNoTracking().FirstOrDefaultAsync(s => s.Slug == slug, ct) ?? throw new AppException(404, Messages.NotFound);
        var available = shop.Status == "Active";
        var stats = available
            ? await Visible().Where(p => p.ShopId == shop.Id).GroupBy(_ => 1)
                .Select(g => new { Count = g.Count(), Rating = g.Where(p => p.ReviewCount > 0).Average(p => (decimal?)p.AverageRating) ?? 0 })
                .FirstOrDefaultAsync(ct)
            : null;
        return new ShopPageDto(shop.Id, shop.Name, shop.Slug, available ? shop.Description : "", shop.LogoUrl is null ? null : Pricing.MediaUrl(shop.LogoUrl),
            shop.BannerUrl is null ? null : Pricing.MediaUrl(shop.BannerUrl), Math.Round(stats?.Rating ?? 0, 1), stats?.Count ?? 0, shop.CreatedAt, available);
    }

    /// <summary>Home data is cached for 60 seconds (FR-5.10).</summary>
    public async Task<HomeDto> HomeAsync(CancellationToken ct = default) =>
        (await cache.GetOrCreateAsync("catalog:home", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
            var now = clock.UtcNow;
            var banners = await db.Banners.AsNoTracking()
                .Where(b => b.IsActive && (b.StartsAt == null || b.StartsAt <= now) && (b.EndsAt == null || b.EndsAt > now))
                .OrderBy(b => b.SortOrder).Take(5)
                .Select(b => new BannerDto(b.Id, b.Title, "/api/v1/media/" + b.ImageUrl, b.LinkUrl)).ToListAsync(ct);
            var newest = await Cards(Visible().OrderByDescending(p => p.CreatedAt).Take(8)).ToListAsync(ct);
            var topRated = await Cards(Visible().Where(p => p.ReviewCount > 0).OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount).Take(8)).ToListAsync(ct);
            return new HomeDto(banners, await categories.TreeAsync(ct: ct), newest, topRated);
        }))!;

    public async Task<IReadOnlyList<string>> BrandsAsync(Guid? categoryId, CancellationToken ct = default) =>
        await Visible().Where(p => p.Brand != null && (categoryId == null || p.CategoryId == categoryId || p.Category.ParentId == categoryId))
            .Select(p => p.Brand!).Distinct().OrderBy(b => b).Take(100).ToListAsync(ct);

    public static IQueryable<ProductListItemDto> Cards(IQueryable<Product> query) => query.Select(p => new ProductListItemDto(
        p.Id, p.Name, p.Brand, p.Price, Math.Round(p.Price * (100 - p.DiscountPercent) / 100, 2), p.DiscountPercent,
        p.ProductImages.OrderBy(i => i.SortOrder).Select(i => "/api/v1/media/" + i.ThumbPath).FirstOrDefault(),
        p.AverageRating, p.ReviewCount, p.Shop.Name, p.Shop.Slug,
        p.ProductVariants.Any(v => v.IsActive) ? p.ProductVariants.Any(v => v.IsActive && v.Stock > 0) : p.Stock > 0));
}
