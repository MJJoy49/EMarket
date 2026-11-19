using EMarket.BLL.Catalog;
using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Cart;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Shopping;

/// <summary>Where a cart line stands right now (FR-6.7). Checkout reads the same thing, so the two always agree.</summary>
public sealed record LineState(CartItem Item, Product? Product, ProductVariant? Variant, decimal UnitPrice, int Stock, string? Problem)
{
    public bool Available => Problem is null;
}

public sealed class CartService(AppDbContext db, ISettingsReader settings, IClock clock)
{
    public const int MaxLines = 20;
    public const int MaxQuantity = 10;
    public const int MaxWishlist = 100;

    public async Task<CartDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var cart = await LoadAsync(userId, ct);
        var states = cart is null ? [] : await StatesAsync(cart, userId, ct);
        var defaultFee = await settings.GetDecimalAsync("deliveryFee", 60, ct);
        var freeAbove = await settings.GetDecimalAsync("freeDeliveryAbove", 2000, ct);

        var shops = states.GroupBy(s => s.Product?.ShopId ?? Guid.Empty).Select(g =>
        {
            var shop = g.First().Product?.Shop;
            var lines = g.Select(s => new CartLineDto(s.Item.Id, s.Item.ProductId, s.Item.VariantId, s.Product?.Name ?? "Removed product",
                s.Variant?.OptionText, s.Product?.ProductImages.OrderBy(i => i.SortOrder).Select(i => Pricing.MediaUrl(i.ThumbPath)).FirstOrDefault(),
                s.UnitPrice, s.Item.Quantity, s.UnitPrice * s.Item.Quantity, s.Stock, s.Available, s.Problem)).ToList();
            var subtotal = lines.Where(l => l.Available).Sum(l => l.LineTotal);
            var fee = shop is null || subtotal == 0 ? 0 : DeliveryFee(shop, subtotal, insideDhaka: true, defaultFee, freeAbove);
            return new CartShopDto(shop?.Id ?? Guid.Empty, shop?.Name ?? "", lines, subtotal, fee);
        }).ToList();

        var itemsTotal = shops.Sum(s => s.Subtotal);
        decimal discount = 0;
        if (cart?.Coupon is { } coupon)
        {
            var uses = await UsesAsync(coupon.Id, userId, ct);
            discount = CouponRules.TryDiscount(coupon, itemsTotal, uses, clock.UtcNow) ?? 0;
        }

        var delivery = shops.Sum(s => s.DeliveryFee);
        return new CartDto(shops, states.Count, itemsTotal, delivery, cart?.Coupon?.Code, discount, itemsTotal + delivery - discount);
    }

    public async Task<int> CountAsync(Guid userId, CancellationToken ct = default) =>
        await db.CartItems.Where(i => i.Cart.UserId == userId).SumAsync(i => (int?)i.Quantity, ct) ?? 0;

    public async Task AddAsync(Guid userId, AddToCartRequest request, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Shop).Include(p => p.ProductVariants).Include(p => p.Category).ThenInclude(c => c.Parent)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, ct) ?? throw new AppException(404, Messages.NotFound);
        if (product.Shop.OwnerId == userId)
        {
            throw new AppException(409, Messages.OwnProduct); // BR-34
        }

        if (product.Shop.Status != "Active")
        {
            throw new AppException(409, Messages.ShopUnavailable);
        }

        if (!IsListed(product))
        {
            throw new AppException(404, Messages.NotFound);
        }

        // FR-5.8: a product with variants needs a chosen variant.
        var activeVariants = product.ProductVariants.Where(v => v.IsActive).ToList();
        ProductVariant? variant = null;
        if (activeVariants.Count > 0)
        {
            variant = activeVariants.FirstOrDefault(v => v.Id == request.VariantId) ?? throw new AppException(400, Messages.Required);
        }
        else if (request.VariantId is not null)
        {
            throw new AppException(400, Messages.Invalid);
        }

        var cart = await LoadAsync(userId, ct) ?? NewCart(userId);
        var line = cart.CartItems.FirstOrDefault(i => i.ProductId == product.Id && i.VariantId == variant?.Id);
        var quantity = (line?.Quantity ?? 0) + request.Quantity;
        var stock = variant?.Stock ?? product.Stock;
        if (quantity > MaxQuantity || quantity > stock)
        {
            throw new AppException(409, string.Format(Messages.NotEnoughStock, product.Name, Math.Min(stock, MaxQuantity))); // BR-37
        }

        if (line is null)
        {
            if (cart.CartItems.Count >= MaxLines)
            {
                throw new AppException(409, Messages.Conflict);
            }

            // Added through the set, not the cart's list: a new row with its id already set must be inserted, not updated.
            db.CartItems.Add(new CartItem { Id = Guid.CreateVersion7(), CartId = cart.Id, ProductId = product.Id, VariantId = variant?.Id, Quantity = quantity });
        }
        else
        {
            line.Quantity = quantity; // FR-6.2: the same item again raises the quantity of the existing line.
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>FR-10.9: copies the items of an old order into the cart, keeping the variant. Items that cannot be bought now are skipped and named.</summary>
    public async Task<IReadOnlyList<string>> ReorderAsync(Guid userId, Guid orderId, CancellationToken ct = default)
    {
        var items = await db.OrderItems.AsNoTracking().Where(i => i.OrderId == orderId && i.Order.BuyerId == userId)
            .Select(i => new { i.ProductId, i.VariantId, i.Quantity, i.ProductName }).ToListAsync(ct);
        if (items.Count == 0)
        {
            throw new AppException(404, Messages.NotFound);
        }

        var skipped = new List<string>();
        foreach (var item in items)
        {
            try
            {
                await AddAsync(userId, new AddToCartRequest { ProductId = item.ProductId, VariantId = item.VariantId, Quantity = Math.Min(item.Quantity, MaxQuantity) }, ct);
            }
            catch (AppException)
            {
                skipped.Add(item.ProductName);
                db.ChangeTracker.Clear();
            }
        }

        return skipped;
    }

    public async Task SetQuantityAsync(Guid userId, Guid lineId, int quantity, CancellationToken ct = default)
    {
        var line = await OwnLineAsync(userId, lineId, ct);
        var stock = line.Variant?.Stock ?? line.Product?.Stock ?? 0;
        if (quantity is < 1 or > MaxQuantity || quantity > stock)
        {
            throw new AppException(409, string.Format(Messages.NotEnoughStock, line.Product?.Name ?? "", Math.Min(stock, MaxQuantity)));
        }

        line.Quantity = quantity;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid userId, Guid lineId, CancellationToken ct = default)
    {
        db.CartItems.Remove(await OwnLineAsync(userId, lineId, ct));
        await db.SaveChangesAsync(ct);
    }

    public async Task<decimal> ApplyCouponAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var cart = await LoadAsync(userId, ct) ?? throw new AppException(409, Messages.BadCoupon);
        var coupon = await FindCouponAsync(code, ct);
        var itemsTotal = (await StatesAsync(cart, userId, ct)).Where(s => s.Available).Sum(s => s.UnitPrice * s.Item.Quantity);
        var discount = CouponRules.TryDiscount(coupon, itemsTotal, await UsesAsync(coupon.Id, userId, ct), clock.UtcNow)
            ?? throw new AppException(400, Messages.BadCoupon);
        cart.CouponId = coupon.Id;
        await db.SaveChangesAsync(ct);
        return discount;
    }

    public async Task RemoveCouponAsync(Guid userId, CancellationToken ct = default)
    {
        if (await LoadAsync(userId, ct) is { } cart)
        {
            cart.CouponId = null;
            await db.SaveChangesAsync(ct);
        }
    }

    // ---------- wishlist (FR-7) ----------

    public async Task<bool> ToggleWishlistAsync(Guid userId, Guid productId, CancellationToken ct = default)
    {
        var existing = await db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId, ct);
        if (existing is not null)
        {
            db.WishlistItems.Remove(existing);
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            throw new AppException(404, Messages.NotFound);
        }

        if (await db.WishlistItems.CountAsync(w => w.UserId == userId, ct) >= MaxWishlist)
        {
            throw new AppException(409, Messages.Conflict); // BR-38
        }

        db.WishlistItems.Add(new WishlistItem { UserId = userId, ProductId = productId });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<WishlistItemDto>> WishlistAsync(Guid userId, CancellationToken ct = default)
    {
        var items = await db.WishlistItems.AsNoTracking().Where(w => w.UserId == userId).OrderByDescending(w => w.CreatedAt)
            .Include(w => w.Product).ThenInclude(p => p.Shop)
            .Include(w => w.Product).ThenInclude(p => p.ProductVariants)
            .Include(w => w.Product).ThenInclude(p => p.ProductImages)
            .Include(w => w.Product).ThenInclude(p => p.Category).ThenInclude(c => c.Parent)
            .ToListAsync(ct);
        return items.Select(w =>
        {
            var p = w.Product;
            if (p is null)
            {
                return new WishlistItemDto(w.ProductId, "Removed product", null, 0, false, false, false, "This product was removed.");
            }

            var variants = p.ProductVariants.Where(v => v.IsActive).ToList();
            var inStock = variants.Count > 0 ? variants.Any(v => v.Stock > 0) : p.Stock > 0;
            var problem = !IsListed(p) || p.Shop.Status != "Active" ? "Not available now." : !inStock ? "Out of stock." : null;
            return new WishlistItemDto(p.Id, p.Name, p.ProductImages.OrderBy(i => i.SortOrder).Select(i => Pricing.MediaUrl(i.ThumbPath)).FirstOrDefault(),
                Pricing.Final(p.Price, p.DiscountPercent), inStock, problem is null, variants.Count > 0, problem);
        }).ToList();
    }

    // ---------- shared with checkout ----------

    public async Task<Cart?> LoadAsync(Guid userId, CancellationToken ct) =>
        await db.Carts.Include(c => c.Coupon).Include(c => c.CartItems).ThenInclude(i => i.Variant)
            .FirstOrDefaultAsync(c => c.UserId == userId, ct);

    /// <summary>Checks every line against the live product, variant, shop and stock (FR-6.7).</summary>
    public async Task<List<LineState>> StatesAsync(Cart cart, Guid userId, CancellationToken ct)
    {
        var ids = cart.CartItems.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id))
            .Include(p => p.Shop).Include(p => p.ProductImages).Include(p => p.ProductVariants).Include(p => p.Category).ThenInclude(c => c.Parent)
            .ToDictionaryAsync(p => p.Id, ct);

        return cart.CartItems.OrderBy(i => i.CreatedAt).Select(item =>
        {
            products.TryGetValue(item.ProductId, out var product);
            var variant = item.VariantId is null ? null : product?.ProductVariants.FirstOrDefault(v => v.Id == item.VariantId);
            var stock = variant?.Stock ?? product?.Stock ?? 0;
            var price = product is null ? 0 : Pricing.Final(variant?.PriceOverride ?? product.Price, product.DiscountPercent);
            string? problem = product switch
            {
                null => "This product was removed.",
                _ when product.Shop.Status != "Active" => Messages.ShopUnavailable,
                _ when product.Shop.OwnerId == userId => Messages.OwnProduct,
                _ when !IsListed(product) || (item.VariantId is not null && variant is not { IsActive: true }) => "This product is not available now.",
                _ when item.VariantId is null && product.ProductVariants.Any(v => v.IsActive) => "Please choose a colour or size again.",
                _ when stock < item.Quantity => string.Format(Messages.NotEnoughStock, product.Name, stock),
                _ => null,
            };
            return new LineState(item, product, variant, price, stock, problem);
        }).ToList();
    }

    public async Task<int> UsesAsync(Guid couponId, Guid userId, CancellationToken ct) =>
        await db.CouponRedemptions.CountAsync(r => r.CouponId == couponId && r.UserId == userId && r.ReleasedAt == null, ct);

    public async Task<Coupon> FindCouponAsync(string code, CancellationToken ct)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await db.Coupons.FirstOrDefaultAsync(c => c.Code.ToUpper() == normalized, ct) ?? throw new AppException(400, Messages.BadCoupon);
    }

    /// <summary>BR-10: each shop sets its own fee inside and outside Dhaka and its free-delivery limit.</summary>
    public static decimal DeliveryFee(Shop shop, decimal subtotal, bool insideDhaka, decimal defaultFee, decimal defaultFreeAbove)
    {
        var freeAbove = shop.FreeDeliveryAbove ?? defaultFreeAbove;
        if (freeAbove > 0 && subtotal > freeAbove) // "free delivery above Tk 2,000": exactly 2,000 still pays
        {
            return 0;
        }

        return (insideDhaka ? shop.InsideDhakaFee : shop.OutsideDhakaFee) ?? defaultFee;
    }

    private static bool IsListed(Product p) =>
        p.Status == "Active" && p.Category.IsActive && p.Category.Parent?.IsActive != false;

    private async Task<CartItem> OwnLineAsync(Guid userId, Guid lineId, CancellationToken ct) =>
        await db.CartItems.Include(i => i.Product).Include(i => i.Variant)
            .FirstOrDefaultAsync(i => i.Id == lineId && i.Cart.UserId == userId, ct) ?? throw new AppException(404, Messages.NotFound);

    private Cart NewCart(Guid userId)
    {
        var cart = new Cart { Id = Guid.CreateVersion7(), UserId = userId };
        db.Carts.Add(cart);
        return cart;
    }
}

/// <summary>BR-15: when a coupon is valid and how much it takes off.</summary>
public static class CouponRules
{
    public static decimal? TryDiscount(Coupon coupon, decimal itemsTotal, int usesByUser, DateTime now)
    {
        var valid = coupon.IsActive && coupon.StartsAt <= now && coupon.ExpiresAt > now
            && itemsTotal > 0 && itemsTotal >= (coupon.MinOrderAmount ?? 0)
            && (coupon.UsageLimit is null || coupon.UsedCount < coupon.UsageLimit)
            && usesByUser < coupon.PerUserLimit;
        if (!valid)
        {
            return null;
        }

        var discount = coupon.DiscountType == "Percent"
            ? Math.Round(itemsTotal * coupon.DiscountValue / 100, 2, MidpointRounding.AwayFromZero)
            : coupon.DiscountValue;
        if (coupon.MaxDiscount is { } cap)
        {
            discount = Math.Min(discount, cap);
        }

        return Math.Min(discount, itemsTotal);
    }
}
