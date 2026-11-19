using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Sellers;

public sealed record BalancesDto(decimal Pending, decimal Available, decimal Reserved, decimal PaidOut)
{
    public bool Negative => Available < 0;
}

public sealed record DailySalesDto(DateOnly Day, decimal Sales);

public sealed record SellerDashboardDto(
    int NewOrders, int ToShip, decimal SalesToday, decimal Sales7Days, decimal Sales30Days, BalancesDto Balances, int LowStock,
    int ReturnsWaiting, int ProductCount, IReadOnlyList<DailySalesDto> Chart);

public sealed record EarningRowDto(DateTime Date, Guid OrderId, string OrderNumber, decimal ItemsTotal, decimal Commission, decimal DeliveryFee, decimal Net, string State);

public sealed record PayoutDto(
    Guid Id, Guid ShopId, string ShopName, decimal Amount, string Method, string Status, string? ReferenceNo, string? RejectionReason,
    DateTime RequestedAt, DateTime? DecidedAt, string? Account, BalancesDto? Balances);

public sealed class PayoutRequest
{
    [Range(typeof(decimal), "1", "100000000", ErrorMessage = Messages.Invalid)]
    public decimal Amount { get; set; }
}

public sealed class PayPayoutRequest
{
    [Required(ErrorMessage = Messages.Required), StringLength(50, MinimumLength = 3, ErrorMessage = Messages.Invalid)]
    public string ReferenceNo { get; set; } = "";

    public DateOnly? PaidOn { get; set; }
}

public sealed record ShopSettingsDto(
    Guid Id, string Name, string Slug, string Description, string? LogoUrl, string? BannerUrl, string Status, decimal InsideDhakaFee,
    decimal OutsideDhakaFee, decimal FreeDeliveryAbove, string PayoutMethod, string PayoutAccountMasked);

public sealed class ShopSettingsRequest
{
    [Required(ErrorMessage = Messages.Required), RegularExpression(@"^[\p{L}\d \-]{3,60}$", ErrorMessage = Messages.Invalid)]
    public string Name { get; set; } = "";

    [StringLength(1000, ErrorMessage = Messages.Invalid)]
    public string? Description { get; set; }

    [Range(typeof(decimal), "0", "1000", ErrorMessage = Messages.Invalid)]
    public decimal InsideDhakaFee { get; set; } = 60;

    [Range(typeof(decimal), "0", "1000", ErrorMessage = Messages.Invalid)]
    public decimal OutsideDhakaFee { get; set; } = 60;

    [Range(typeof(decimal), "0", "100000", ErrorMessage = Messages.Invalid)]
    public decimal FreeDeliveryAbove { get; set; } = 2000;

    /// <summary>FR-15.11: on vacation the shop's products are hidden; open orders go on.</summary>
    public bool Vacation { get; set; }
}

public sealed class PayoutDetailsRequest
{
    [Required(ErrorMessage = Messages.Required)]
    public string CurrentPassword { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression("^(BankTransfer|bKash|Nagad)$", ErrorMessage = Messages.Invalid)]
    public string PayoutMethod { get; set; } = "";

    [RegularExpression(ValidationPatterns.Phone, ErrorMessage = Messages.Invalid)]
    public string? WalletNumber { get; set; }

    [StringLength(80, ErrorMessage = Messages.Invalid)]
    public string? BankName { get; set; }

    [StringLength(80, ErrorMessage = Messages.Invalid)]
    public string? BankBranch { get; set; }

    [RegularExpression(@"^\d{9}$", ErrorMessage = Messages.Invalid)]
    public string? RoutingNumber { get; set; }

    [StringLength(100, ErrorMessage = Messages.Invalid)]
    public string? AccountName { get; set; }

    [RegularExpression(@"^\d{6,20}$", ErrorMessage = Messages.Invalid)]
    public string? AccountNumber { get; set; }
}
