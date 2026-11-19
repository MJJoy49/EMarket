using EMarket.BLL.Common;
using EMarket.DAL.Data;
using EMarket.Domain.Entities;
using EMarket.Shared.Constants;
using EMarket.Shared.Dtos.Catalog;
using EMarket.Shared.Results;
using Microsoft.EntityFrameworkCore;

namespace EMarket.BLL.Catalog;

/// <summary>Seller side of products (FR-4) and the admin approval queue (FR-17).</summary>
public sealed class ProductService(AppDbContext db, CategoryService categories, IFileStorage files, Notifier notifier)
{
    public const int MaxImages = 5;
    private static readonly string[] OpenOrderStatuses = ["AwaitingPayment", "Pending", "Confirmed", "Processing", "Shipped"];

    // ---------- seller ----------

    public async Task<PagedResult<SellerProductDto>> ListOwnAsync(Guid sellerId, string? status, string? search, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var shopId = await OwnShopIdAsync(sellerId, ct);
        var query = db.Products.AsNoTracking().Where(p => p.ShopId == shopId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(term));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(p => p.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new
            {
                p.Id, p.Name, Category = p.Category.Name, p.Price, p.DiscountPercent, p.Status, p.RejectionReason, p.AverageRating,
                Stock = p.ProductVariants.Any() ? p.ProductVariants.Sum(v => v.Stock) : p.Stock,
                Thumb = p.ProductImages.OrderBy(i => i.SortOrder).Select(i => i.ThumbPath).FirstOrDefault(),
                Sold = p.OrderItems.Where(i => i.Order.Status == "Delivered").Sum(i => (int?)(i.Quantity - i.ReturnedQuantity)) ?? 0,
            })
            .ToListAsync(ct);
        var hidden = await HiddenByAdminAsync(rows.Select(r => r.Id).ToList(), ct);
        var items = rows.Select(r => new SellerProductDto(r.Id, r.Name, r.Category, r.Price, Pricing.Final(r.Price, r.DiscountPercent), r.Stock,
            r.Status, r.RejectionReason, r.Thumb is null ? null : Pricing.MediaUrl(r.Thumb), r.Sold, r.AverageRating, hidden.Contains(r.Id))).ToList();
        return new PagedResult<SellerProductDto>(items, page, pageSize, total);
    }

    public async Task<SellerProductDetailDto> GetOwnAsync(Guid sellerId, Guid productId, CancellationToken ct = default)
    {
        var product = await OwnProductAsync(sellerId, productId, ct, tracking: false);
        var discountPrice = product.DiscountPercent > 0 ? Pricing.Final(product.Price, product.DiscountPercent) : (decimal?)null;
        var hidden = (await HiddenByAdminAsync([productId], ct)).Contains(productId);
        return new SellerProductDetailDto(product.Id, product.Name, product.CategoryId, product.Brand, product.Description, product.Price,
            discountPrice, product.Stock, product.Status, product.RejectionReason, hidden, Images(product), Variants(product));
    }

    public async Task<Guid> CreateAsync(Guid sellerId, SaveProductRequest request, CancellationToken ct = default)
    {
        var shop = await ActiveShopAsync(sellerId, ct);
        await categories.ActiveLeafAsync(request.CategoryId!.Value, ct);
        Validate(request);
        await CheckSkusAsync(shop.Id, null, request.Variants, ct);

        var product = new Product { Id = Guid.CreateVersion7(), ShopId = shop.Id, Status = "Draft" };
        ApplyFields(product, request);
        ApplyVariants(product, request.Variants);
        // A new product has no image yet, so it always starts as a draft; images are added next, then it is submitted.
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product.Id;
    }

    public async Task UpdateAsync(Guid sellerId, Guid productId, SaveProductRequest request, CancellationToken ct = default)
    {
        var shop = await ActiveShopAsync(sellerId, ct);
        var product = await OwnProductAsync(sellerId, productId, ct);
        await categories.ActiveLeafAsync(request.CategoryId!.Value, ct);
        Validate(request);
        await CheckSkusAsync(shop.Id, product.Id, request.Variants, ct);

        // FR-4.6: a change to name, description or category of a live product needs approval again; price and stock do not.
        var textChanged = product.Name != request.Name.Trim() || product.Description != request.Description.Trim()
            || product.CategoryId != request.CategoryId || product.Brand != request.Brand?.Trim();
        ApplyFields(product, request);
        ApplyVariants(product, request.Variants);
        if (product.Status == "Active" && textChanged)
        {
            await SendForApprovalAsync(product, shop.Name, ct);
        }
        else if (request.Submit && product.Status is "Draft" or "Rejected")
        {
            await SendForApprovalAsync(product, shop.Name, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(Guid sellerId, Guid productId, CancellationToken ct = default)
    {
        var shop = await ActiveShopAsync(sellerId, ct);
        var product = await OwnProductAsync(sellerId, productId, ct);
        if (product.Status is not ("Draft" or "Rejected"))
        {
            throw new AppException(409, Messages.Conflict);
        }

        await SendForApprovalAsync(product, shop.Name, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProductImageDto> AddImageAsync(Guid sellerId, Guid productId, UploadFile file, CancellationToken ct = default)
    {
        var shop = await ActiveShopAsync(sellerId, ct);
        var product = await OwnProductAsync(sellerId, productId, ct);
        if (product.ProductImages.Count >= MaxImages)
        {
            throw new AppException(400, Messages.BadFile);
        }

        var saved = await files.SaveImageWithThumbnailAsync(file, "products", ct);
        var image = new ProductImage
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Path = saved.Path,
            ThumbPath = saved.ThumbPath,
            SortOrder = Enumerable.Range(1, MaxImages).First(n => product.ProductImages.All(i => i.SortOrder != n)),
        };
        db.ProductImages.Add(image);
        if (product.Status == "Active")
        {
            await SendForApprovalAsync(product, shop.Name, ct);
        }

        await db.SaveChangesAsync(ct);
        return new ProductImageDto(image.Id, Pricing.MediaUrl(image.Path), Pricing.MediaUrl(image.ThumbPath), image.SortOrder);
    }

    public async Task DeleteImageAsync(Guid sellerId, Guid productId, Guid imageId, CancellationToken ct = default)
    {
        var shop = await ActiveShopAsync(sellerId, ct);
        var product = await OwnProductAsync(sellerId, productId, ct);
        var image = product.ProductImages.FirstOrDefault(i => i.Id == imageId) ?? throw new AppException(404, Messages.NotFound);
        if (product.Status is "Active" or "Pending" && product.ProductImages.Count == 1)
        {
            throw new AppException(409, Messages.Conflict); // A listed product keeps at least one image (FR-4.3).
        }

        db.ProductImages.Remove(image);
        if (product.Status == "Active")
        {
            await SendForApprovalAsync(product, shop.Name, ct);
        }

        await db.SaveChangesAsync(ct);
        files.Delete(image.Path);
        files.Delete(image.ThumbPath);
    }

    /// <summary>FR-4.7: hiding and unhiding needs no new approval, but a product the admin hid stays hidden.</summary>
    public async Task SetHiddenAsync(Guid sellerId, Guid productId, bool hidden, CancellationToken ct = default)
    {
        var product = await OwnProductAsync(sellerId, productId, ct);
        if (hidden && product.Status == "Active")
        {
            product.Status = "Hidden";
        }
        else if (!hidden && product.Status == "Hidden" && !(await HiddenByAdminAsync([productId], ct)).Contains(productId))
        {
            product.Status = "Active";
        }
        else
        {
            throw new AppException(409, Messages.Conflict);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateStockAsync(Guid sellerId, Guid productId, StockRequest request, CancellationToken ct = default)
    {
        var product = await OwnProductAsync(sellerId, productId, ct);
        if (request.VariantId is { } variantId)
        {
            var variant = product.ProductVariants.FirstOrDefault(v => v.Id == variantId) ?? throw new AppException(404, Messages.NotFound);
            variant.Stock = request.Stock;
        }
        else if (product.ProductVariants.Count == 0)
        {
            product.Stock = request.Stock;
        }
        else
        {
            throw new AppException(400, Messages.Invalid);
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>FR-4.8: soft delete, refused while an order is still open. Old orders keep their own copy.</summary>
    public async Task DeleteAsync(Guid sellerId, Guid productId, CancellationToken ct = default)
    {
        var product = await OwnProductAsync(sellerId, productId, ct);
        await EnsureNoOpenOrderAsync(productId, ct);
        product.IsDeleted = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>FR-4.13: a copy is a new draft with its own variants and no shared images.</summary>
    public async Task<Guid> CopyAsync(Guid sellerId, Guid productId, CancellationToken ct = default)
    {
        var source = await OwnProductAsync(sellerId, productId, ct, tracking: false);
        var copy = new Product
        {
            Id = Guid.CreateVersion7(),
            ShopId = source.ShopId,
            CategoryId = source.CategoryId,
            Name = $"{source.Name} (copy)"[..Math.Min(150, source.Name.Length + 7)],
            Brand = source.Brand,
            Description = source.Description,
            Price = source.Price,
            DiscountPercent = source.DiscountPercent,
            Stock = source.Stock,
            Status = "Draft",
        };
        foreach (var v in source.ProductVariants)
        {
            copy.ProductVariants.Add(new ProductVariant
            {
                Id = Guid.CreateVersion7(), Sku = $"{v.Sku}-C"[..Math.Min(40, v.Sku.Length + 2)], OptionText = v.OptionText,
                PriceOverride = v.PriceOverride, Stock = 0, IsActive = v.IsActive,
            });
        }

        await CheckSkusAsync(source.ShopId, null, copy.ProductVariants.Select(v => new VariantInput { Sku = v.Sku }).ToList(), ct);
        db.Products.Add(copy);
        await db.SaveChangesAsync(ct);
        return copy.Id;
    }

    // ---------- admin ----------

    public async Task<PagedResult<ApprovalQueueItemDto>> AdminListAsync(string? status, Guid? shopId, Guid? categoryId, int page, CancellationToken ct = default)
    {
        const int pageSize = 20;
        page = Math.Max(1, page);
        var query = db.Products.AsNoTracking().AsQueryable();
        query = string.IsNullOrWhiteSpace(status) ? query.Where(p => p.Status != "Draft") : query.Where(p => p.Status == status);
        if (shopId is not null)
        {
            query = query.Where(p => p.ShopId == shopId);
        }

        if (categoryId is not null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        // The approval queue is oldest first (FR-17.1); other lists are newest first.
        query = status == "Pending" ? query.OrderBy(p => p.UpdatedAt) : query.OrderByDescending(p => p.UpdatedAt);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ApprovalQueueItemDto(p.Id, p.Name, p.Shop.Name, p.Category.Name, p.Price, p.Status, p.UpdatedAt,
                p.ProductImages.OrderBy(i => i.SortOrder).Select(i => "/api/v1/media/" + i.ThumbPath).FirstOrDefault()))
            .ToListAsync(ct);
        return new PagedResult<ApprovalQueueItemDto>(items, page, pageSize, total);
    }

    public async Task<SellerProductDetailDto> AdminGetAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await Load(tracking: false).FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw new AppException(404, Messages.NotFound);
        var hidden = (await HiddenByAdminAsync([productId], ct)).Contains(productId);
        return new SellerProductDetailDto(product.Id, product.Name, product.CategoryId, product.Brand, product.Description, product.Price,
            product.DiscountPercent > 0 ? Pricing.Final(product.Price, product.DiscountPercent) : null, product.Stock, product.Status,
            product.RejectionReason, hidden, Images(product), Variants(product));
    }

    public async Task ApproveAsync(Guid adminId, Guid productId, CancellationToken ct = default)
    {
        var product = await AdminProductAsync(productId, ct);
        if (product.Status != "Pending")
        {
            throw new AppException(409, Messages.Conflict);
        }

        product.Status = "Active";
        product.RejectionReason = null;
        notifier.Notify(product.Shop.OwnerId, "ProductApproved", "Product approved", $"{product.Name} is now live.", $"/seller/products/{product.Id}");
        notifier.Email(product.Shop.Owner.Email, "Your product was approved", $"{product.Name} is now visible to buyers.");
        notifier.Audit(adminId, "ProductApproved", nameof(Product), product.Id);
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid adminId, Guid productId, string reason, CancellationToken ct = default)
    {
        var product = await AdminProductAsync(productId, ct);
        if (product.Status != "Pending")
        {
            throw new AppException(409, Messages.Conflict);
        }

        product.Status = "Rejected";
        product.RejectionReason = reason.Trim();
        notifier.Notify(product.Shop.OwnerId, "ProductRejected", "Product rejected", $"{product.Name}: {product.RejectionReason}", $"/seller/products/{product.Id}");
        notifier.Email(product.Shop.Owner.Email, "Your product was rejected", $"{product.Name} was rejected. Reason: {product.RejectionReason}");
        notifier.Audit(adminId, "ProductRejected", nameof(Product), product.Id, newValue: product.RejectionReason);
        await db.SaveChangesAsync(ct);
    }

    public async Task AdminSetHiddenAsync(Guid adminId, Guid productId, bool hidden, string? reason, CancellationToken ct = default)
    {
        var product = await AdminProductAsync(productId, ct);
        if (hidden && product.Status is "Active" or "Hidden")
        {
            product.Status = "Hidden";
            notifier.Notify(product.Shop.OwnerId, "ProductHidden", "Product hidden by admin", $"{product.Name}: {reason}");
            notifier.Audit(adminId, "ProductHiddenByAdmin", nameof(Product), product.Id, newValue: reason);
        }
        else if (!hidden && product.Status == "Hidden")
        {
            product.Status = "Active";
            notifier.Audit(adminId, "ProductUnhiddenByAdmin", nameof(Product), product.Id, newValue: reason);
        }
        else
        {
            throw new AppException(409, Messages.Conflict);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AdminDeleteAsync(Guid adminId, Guid productId, string reason, CancellationToken ct = default)
    {
        var product = await AdminProductAsync(productId, ct);
        await EnsureNoOpenOrderAsync(productId, ct);
        product.IsDeleted = true;
        notifier.Notify(product.Shop.OwnerId, "ProductDeleted", "Product removed by admin", $"{product.Name}: {reason}");
        notifier.Audit(adminId, "ProductDeletedByAdmin", nameof(Product), product.Id, newValue: reason);
        await db.SaveChangesAsync(ct);
    }

    // ---------- helpers ----------

    private async Task SendForApprovalAsync(Product product, string shopName, CancellationToken ct)
    {
        if (product.ProductImages.Count == 0 && !db.ProductImages.Local.Any(i => i.ProductId == product.Id))
        {
            throw new AppException(400, "Add at least one image, then submit the product for approval.");
        }

        product.Status = "Pending";
        product.RejectionReason = null;
        await notifier.NotifyAdminsAsync("ProductSubmitted", "Product waiting for approval", $"{shopName}: {product.Name}", $"/admin/products/{product.Id}", ct);
        notifier.Notify((await db.Shops.AsNoTracking().FirstAsync(s => s.Id == product.ShopId, ct)).OwnerId, "ProductSubmitted",
            "Product submitted", Messages.ProductSubmitted, $"/seller/products/{product.Id}");
    }

    private static void Validate(SaveProductRequest request)
    {
        if (request.DiscountPrice is { } discount && discount >= request.Price)
        {
            throw new AppException(400, Messages.Invalid); // BR-6
        }

        if (request.Variants.Any(v => string.IsNullOrWhiteSpace(v.Colour) && string.IsNullOrWhiteSpace(v.Size)))
        {
            throw new AppException(400, Messages.Invalid); // a variant needs a colour or a size (SRS 4.7)
        }

        if (request.Variants.Select(v => v.Sku.Trim().ToLower()).Distinct().Count() != request.Variants.Count)
        {
            throw new AppException(409, Messages.Conflict);
        }
    }

    // FR-4.4: a SKU is unique within the whole shop, not only within one product.
    private async Task CheckSkusAsync(Guid shopId, Guid? productId, IReadOnlyCollection<VariantInput> variants, CancellationToken ct)
    {
        var skus = variants.Select(v => v.Sku.Trim().ToLower()).ToList();
        if (skus.Count > 0 && await db.ProductVariants.AnyAsync(
                v => v.Product.ShopId == shopId && v.ProductId != productId && !v.Product.IsDeleted && skus.Contains(v.Sku.ToLower()), ct))
        {
            throw new AppException(409, Messages.Conflict);
        }
    }

    private static void ApplyFields(Product product, SaveProductRequest request)
    {
        product.Name = request.Name.Trim();
        product.CategoryId = request.CategoryId!.Value;
        product.Brand = string.IsNullOrWhiteSpace(request.Brand) ? null : request.Brand.Trim();
        product.Description = request.Description.Trim();
        product.Price = request.Price;
        product.DiscountPercent = Pricing.PercentFrom(request.Price, request.DiscountPrice);
        product.Stock = request.Variants.Count == 0 ? request.Stock : 0;
    }

    // Variants that are left out of the form are switched off, not deleted, because old orders point to them.
    private static void ApplyVariants(Product product, List<VariantInput> inputs)
    {
        var keep = new HashSet<Guid>();
        foreach (var input in inputs)
        {
            var variant = input.Id is { } id ? product.ProductVariants.FirstOrDefault(v => v.Id == id) : null;
            if (variant is null)
            {
                variant = new ProductVariant { Id = Guid.CreateVersion7(), ProductId = product.Id };
                product.ProductVariants.Add(variant);
            }

            variant.Sku = input.Sku.Trim();
            variant.OptionText = string.Join(" / ", new[] { input.Colour?.Trim(), input.Size?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));
            variant.Stock = input.Stock;
            variant.PriceOverride = input.Price;
            variant.IsActive = true;
            keep.Add(variant.Id);
        }

        foreach (var variant in product.ProductVariants.Where(v => !keep.Contains(v.Id)))
        {
            variant.IsActive = false;
        }
    }

    private async Task EnsureNoOpenOrderAsync(Guid productId, CancellationToken ct)
    {
        if (await db.OrderItems.AnyAsync(i => i.ProductId == productId && OpenOrderStatuses.Contains(i.Order.Status), ct))
        {
            throw new AppException(409, Messages.Conflict);
        }
    }

    private async Task<HashSet<Guid>> HiddenByAdminAsync(List<Guid> productIds, CancellationToken ct)
    {
        var last = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == nameof(Product) && a.EntityId != null && productIds.Contains(a.EntityId.Value)
                && (a.Action == "ProductHiddenByAdmin" || a.Action == "ProductUnhiddenByAdmin"))
            .GroupBy(a => a.EntityId!.Value)
            .Select(g => g.OrderByDescending(a => a.CreatedAt).First())
            .ToListAsync(ct);
        var hidden = last.Where(a => a.Action == "ProductHiddenByAdmin").Select(a => a.EntityId!.Value).ToHashSet();
        var stillHidden = await db.Products.AsNoTracking().Where(p => hidden.Contains(p.Id) && p.Status == "Hidden").Select(p => p.Id).ToListAsync(ct);
        return stillHidden.ToHashSet();
    }

    private async Task<Guid> OwnShopIdAsync(Guid sellerId, CancellationToken ct) =>
        await db.Shops.Where(s => s.OwnerId == sellerId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct)
        ?? throw new AppException(403, Messages.NotAllowed);

    private async Task<Shop> ActiveShopAsync(Guid sellerId, CancellationToken ct)
    {
        var shop = await db.Shops.FirstOrDefaultAsync(s => s.OwnerId == sellerId, ct) ?? throw new AppException(403, Messages.NotAllowed);
        return shop.Status == "Active" ? shop : throw new AppException(403, Messages.ShopUnavailable);
    }

    // FR-4.16: another seller's product answers 403 (the caller logs it via the error middleware).
    private async Task<Product> OwnProductAsync(Guid sellerId, Guid productId, CancellationToken ct, bool tracking = true)
    {
        var product = await Load(tracking).Include(p => p.Shop).FirstOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new AppException(404, Messages.NotFound);
        return product.Shop.OwnerId == sellerId ? product : throw new AppException(403, Messages.NotAllowed);
    }

    private async Task<Product> AdminProductAsync(Guid productId, CancellationToken ct) =>
        await db.Products.Include(p => p.Shop).ThenInclude(s => s.Owner).Include(p => p.ProductImages)
            .FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw new AppException(404, Messages.NotFound);

    private IQueryable<Product> Load(bool tracking)
    {
        var query = db.Products.Include(p => p.ProductImages).Include(p => p.ProductVariants);
        return tracking ? query : query.AsNoTracking();
    }

    private static List<ProductImageDto> Images(Product p) => p.ProductImages.OrderBy(i => i.SortOrder)
        .Select(i => new ProductImageDto(i.Id, Pricing.MediaUrl(i.Path), Pricing.MediaUrl(i.ThumbPath), i.SortOrder)).ToList();

    private static List<VariantDto> Variants(Product p) => p.ProductVariants.OrderBy(v => v.OptionText)
        .Select(v => new VariantDto(v.Id, v.Sku, v.OptionText, v.PriceOverride ?? p.Price,
            Pricing.Final(v.PriceOverride ?? p.Price, p.DiscountPercent), v.Stock, v.IsActive)).ToList();
}
