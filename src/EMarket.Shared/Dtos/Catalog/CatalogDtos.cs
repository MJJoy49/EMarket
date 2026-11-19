using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Catalog;

public sealed record CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId, int SortOrder, bool IsActive, IReadOnlyList<CategoryDto> Children);

public sealed class SaveCategoryRequest
{
    [Required(ErrorMessage = Messages.Required), StringLength(60, MinimumLength = 2, ErrorMessage = Messages.Invalid)]
    public string Name { get; set; } = "";

    public Guid? ParentId { get; set; }

    [Range(0, 10_000, ErrorMessage = Messages.Invalid)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed record ProductListItemDto(
    Guid Id, string Name, string? Brand, decimal Price, decimal FinalPrice, decimal DiscountPercent, string? ThumbUrl,
    decimal AverageRating, int ReviewCount, string ShopName, string ShopSlug, bool InStock);

public sealed record VariantDto(Guid Id, string Sku, string OptionText, decimal Price, decimal FinalPrice, int Stock, bool IsActive);

public sealed record ProductImageDto(Guid Id, string Url, string ThumbUrl, int SortOrder);

public sealed record ProductDetailDto(
    Guid Id, string Name, string? Brand, string Description, decimal Price, decimal FinalPrice, decimal DiscountPercent,
    int Stock, bool InStock, decimal AverageRating, int ReviewCount, Guid CategoryId, string CategoryName,
    Guid ShopId, string ShopName, string ShopSlug, IReadOnlyList<ProductImageDto> Images, IReadOnlyList<VariantDto> Variants);

public sealed class ProductFilter
{
    [StringLength(100, MinimumLength = 2, ErrorMessage = Messages.Invalid)]
    public string? Q { get; set; }

    public Guid? CategoryId { get; set; }

    [Range(0, 10_000_000, ErrorMessage = Messages.Invalid)]
    public decimal? MinPrice { get; set; }

    [Range(0, 10_000_000, ErrorMessage = Messages.Invalid)]
    public decimal? MaxPrice { get; set; }

    public string? Brand { get; set; }

    [Range(0, 5, ErrorMessage = Messages.Invalid)]
    public int? MinRating { get; set; }

    public bool InStockOnly { get; set; }

    [RegularExpression("^(newest|price-asc|price-desc|top-rated)$", ErrorMessage = Messages.Invalid)]
    public string Sort { get; set; } = "newest";

    [Range(1, 10_000, ErrorMessage = Messages.Invalid)]
    public int Page { get; set; } = 1;

    public Guid? ShopId { get; set; }
}

public sealed record ShopPageDto(Guid Id, string Name, string Slug, string Description, string? LogoUrl, string? BannerUrl,
    decimal Rating, int ProductCount, DateTime JoinedAt, bool Available);

public sealed record BannerDto(Guid Id, string Title, string ImageUrl, string? LinkUrl);

public sealed record HomeDto(
    IReadOnlyList<BannerDto> Banners, IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<ProductListItemDto> NewArrivals, IReadOnlyList<ProductListItemDto> TopRated);

public sealed class VariantInput
{
    public Guid? Id { get; set; }

    [StringLength(30, ErrorMessage = Messages.Invalid)]
    public string? Colour { get; set; }

    [StringLength(30, ErrorMessage = Messages.Invalid)]
    public string? Size { get; set; }

    [Required(ErrorMessage = Messages.Required), StringLength(40, MinimumLength = 3, ErrorMessage = Messages.Invalid)]
    public string Sku { get; set; } = "";

    [Range(0, 100_000, ErrorMessage = Messages.Invalid)]
    public int Stock { get; set; }

    [Range(typeof(decimal), "1", "10000000", ErrorMessage = Messages.Invalid)]
    public decimal? Price { get; set; }
}

public sealed class SaveProductRequest
{
    [Required(ErrorMessage = Messages.Required), StringLength(150, MinimumLength = 3, ErrorMessage = Messages.Invalid)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = Messages.Required)]
    public Guid? CategoryId { get; set; }

    [StringLength(100, ErrorMessage = Messages.Invalid)]
    public string? Brand { get; set; }

    // The column holds 4000 characters, so the form cannot allow more (SRS 6.1, text length rule).
    [Required(ErrorMessage = Messages.Required), StringLength(4000, ErrorMessage = Messages.Invalid)]
    public string Description { get; set; } = "";

    [Range(typeof(decimal), "1", "10000000", ErrorMessage = Messages.Invalid)]
    public decimal Price { get; set; }

    [Range(typeof(decimal), "1", "10000000", ErrorMessage = Messages.Invalid)]
    public decimal? DiscountPrice { get; set; }

    [Range(0, 100_000, ErrorMessage = Messages.Invalid)]
    public int Stock { get; set; }

    public List<VariantInput> Variants { get; set; } = [];

    /// <summary>True sends the product for approval; false keeps it as a draft (FR-4.14).</summary>
    public bool Submit { get; set; }
}

public sealed record SellerProductDto(
    Guid Id, string Name, string CategoryName, decimal Price, decimal FinalPrice, int Stock, string Status, string? RejectionReason,
    string? ThumbUrl, int UnitsSold, decimal AverageRating, bool HiddenByAdmin);

public sealed record SellerProductDetailDto(
    Guid Id, string Name, Guid CategoryId, string? Brand, string Description, decimal Price, decimal? DiscountPrice, int Stock,
    string Status, string? RejectionReason, bool HiddenByAdmin, IReadOnlyList<ProductImageDto> Images, IReadOnlyList<VariantDto> Variants);

public sealed record ApprovalQueueItemDto(
    Guid Id, string Name, string ShopName, string CategoryName, decimal Price, string Status, DateTime UpdatedAt, string? ThumbUrl);

public sealed class ReasonRequest
{
    [Required(ErrorMessage = Messages.Required), StringLength(500, MinimumLength = 10, ErrorMessage = Messages.Invalid)]
    public string Reason { get; set; } = "";
}

public sealed class StockRequest
{
    public Guid? VariantId { get; set; }

    [Range(0, 100_000, ErrorMessage = Messages.Invalid)]
    public int Stock { get; set; }
}
