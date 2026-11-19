using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Cart;

public sealed class AddToCartRequest
{
    [Required(ErrorMessage = Messages.Required)]
    public Guid? ProductId { get; set; }

    public Guid? VariantId { get; set; }

    [Range(1, 10, ErrorMessage = Messages.Invalid)]
    public int Quantity { get; set; } = 1;
}

public sealed class QuantityRequest
{
    [Range(1, 10, ErrorMessage = Messages.Invalid)]
    public int Quantity { get; set; }
}

public sealed class CouponRequest
{
    [Required(ErrorMessage = Messages.Required), RegularExpression("^[A-Za-z0-9]{4,20}$", ErrorMessage = Messages.Invalid)]
    public string Code { get; set; } = "";
}

public sealed record CartLineDto(
    Guid Id, Guid ProductId, Guid? VariantId, string ProductName, string? VariantText, string? ThumbUrl, decimal UnitPrice,
    int Quantity, decimal LineTotal, int Stock, bool Available, string? Problem);

public sealed record CartShopDto(Guid ShopId, string ShopName, IReadOnlyList<CartLineDto> Lines, decimal Subtotal, decimal DeliveryFee);

public sealed record CartDto(
    IReadOnlyList<CartShopDto> Shops, int LineCount, decimal Subtotal, decimal DeliveryTotal, string? CouponCode, decimal Discount, decimal Total);

public sealed record WishlistItemDto(Guid ProductId, string Name, string? ThumbUrl, decimal FinalPrice, bool InStock, bool Available, bool HasVariants, string? Problem);
