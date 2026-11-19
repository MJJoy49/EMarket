using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Checkout;

public sealed class CheckoutRequest
{
    [Required(ErrorMessage = Messages.Required)]
    public Guid? AddressId { get; set; }

    [Required(ErrorMessage = Messages.Required), RegularExpression("^(COD|bKash|Nagad|Card)$", ErrorMessage = Messages.Invalid)]
    public string PaymentMethod { get; set; } = "COD";

    public bool UseStoreCredit { get; set; }

    [RegularExpression("^[A-Za-z0-9]{4,20}$", ErrorMessage = Messages.Invalid)]
    public string? CouponCode { get; set; }
}

public sealed record CheckoutShopDto(Guid ShopId, string ShopName, decimal ItemsTotal, decimal DeliveryFee, decimal Discount);

public sealed record CheckoutSummaryDto(
    IReadOnlyList<CheckoutShopDto> Shops, decimal ItemsTotal, decimal DeliveryTotal, decimal Discount, decimal StoreCreditBalance,
    decimal CreditUsed, decimal Payable, bool CodAllowed, decimal CodLimit, string? CouponMessage);

public sealed record PlacedOrderDto(Guid Id, string OrderNumber, string ShopName, decimal Total, string Status);

public sealed record CheckoutResultDto(
    Guid CheckoutGroupId, IReadOnlyList<PlacedOrderDto> Orders, decimal Payable, string PaymentMethod, string Status,
    bool AwaitingPayment, DateTime? PayBefore, bool Repeated);
