using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Orders;

public sealed record OrderListItemDto(
    Guid Id, string OrderNumber, DateTime CreatedAt, string ShopName, string BuyerName, decimal Total, string Status, string PaymentStatus,
    string PaymentMethod, Guid CheckoutGroupId, bool CanPay, bool CanCancel);

public sealed record OrderItemDto(Guid Id, Guid ProductId, string ProductName, string? VariantText, decimal UnitPrice, int Quantity, int ReturnedQuantity, bool Reviewed);

public sealed record StatusChangeDto(string? From, string To, DateTime At, string? Note);

public sealed record PaymentInfoDto(Guid Id, string Method, decimal Amount, string Status, string? TransactionId, DateTime CreatedAt, DateTime? PaidAt);

public sealed record RefundInfoDto(Guid Id, decimal Amount, string Method, string Status, DateTime CreatedAt);

public sealed record OrderDetailDto(
    Guid Id, string OrderNumber, DateTime CreatedAt, string Status, Guid ShopId, string ShopName, string BuyerName, string ShipName,
    string ShipPhone, string ShipAddress, decimal ItemsTotal, decimal DeliveryFee, decimal Discount, decimal CreditUsed, decimal Total,
    string? CourierName, string? TrackingNote, DateTime? DeliveredAt, string PaymentMethod, string PaymentStatus,
    IReadOnlyList<OrderItemDto> Items, IReadOnlyList<StatusChangeDto> History, IReadOnlyList<PaymentInfoDto> Payments,
    IReadOnlyList<RefundInfoDto> Refunds, IReadOnlyList<string> AllowedActions, DateTime? PayBefore, Guid CheckoutGroupId);

public sealed class CancelRequest
{
    [Required(ErrorMessage = Messages.Required), StringLength(200, MinimumLength = 5, ErrorMessage = Messages.Invalid)]
    public string Reason { get; set; } = "";
}

public sealed class ShipRequest
{
    [StringLength(60, ErrorMessage = Messages.Invalid)]
    public string? CourierName { get; set; }

    [StringLength(200, ErrorMessage = Messages.Invalid)]
    public string? TrackingNote { get; set; }
}

public sealed record PaymentStartDto(string GatewayUrl, string TransactionId, DateTime PayBefore);
