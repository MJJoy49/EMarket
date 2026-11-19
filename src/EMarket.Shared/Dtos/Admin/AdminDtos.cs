using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Admin;

public sealed record UserRowDto(Guid Id, string FullName, string Email, string Phone, IReadOnlyList<string> Roles, string Status, DateTime JoinedAt);

public sealed record UserDetailDto(
    Guid Id, string FullName, string Email, string Phone, IReadOnlyList<string> Roles, string Status, DateTime JoinedAt, DateTime? LastLogin,
    int OrderCount, bool EmailConfirmed, string? ShopName);

public sealed class CreateAdminRequest
{
    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Name, ErrorMessage = Messages.Invalid)]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), EmailAddress(ErrorMessage = Messages.Invalid), StringLength(150)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Phone, ErrorMessage = Messages.Invalid)]
    public string Phone { get; set; } = "";
}

public sealed record ShopRowDto(Guid Id, string Name, string Slug, string OwnerName, string Status, decimal Rating, decimal Sales, DateTime CreatedAt);

public sealed record AuditRowDto(DateTime At, string? UserName, string Action, string EntityName, Guid? EntityId, string? OldValue, string? NewValue);

public sealed record CouponDto(
    Guid Id, string Code, string DiscountType, decimal DiscountValue, decimal? MaxDiscount, decimal? MinOrderAmount, DateTime StartsAt,
    DateTime ExpiresAt, int? UsageLimit, int PerUserLimit, int UsedCount, bool IsActive, string State);

public sealed class SaveCouponRequest : IValidatableObject
{
    [Required(ErrorMessage = Messages.Required), RegularExpression("^[A-Za-z0-9]{4,20}$", ErrorMessage = Messages.Invalid)]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), RegularExpression("^(Percent|Fixed)$", ErrorMessage = Messages.Invalid)]
    public string DiscountType { get; set; } = "Percent";

    [Range(typeof(decimal), "1", "100000", ErrorMessage = Messages.Invalid)]
    public decimal DiscountValue { get; set; }

    [Range(typeof(decimal), "1", "100000", ErrorMessage = Messages.Invalid)]
    public decimal? MaxDiscount { get; set; }

    [Range(typeof(decimal), "0", "10000000", ErrorMessage = Messages.Invalid)]
    public decimal? MinOrderAmount { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    [Range(1, 1_000_000, ErrorMessage = Messages.Invalid)]
    public int? UsageLimit { get; set; }

    [Range(1, 1000, ErrorMessage = Messages.Invalid)]
    public int PerUserLimit { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DiscountType == "Percent" && DiscountValue > 100)
        {
            yield return new ValidationResult(Messages.Invalid, [nameof(DiscountValue)]);
        }

        if (ExpiresAt <= StartsAt)
        {
            yield return new ValidationResult(Messages.Invalid, [nameof(ExpiresAt)]);
        }
    }
}

public sealed record BannerAdminDto(Guid Id, string Title, string ImageUrl, string? LinkUrl, int SortOrder, DateTime? StartsAt, DateTime? EndsAt, bool IsActive);

public sealed class SaveBannerRequest
{
    [Required(ErrorMessage = Messages.Required), StringLength(80, ErrorMessage = Messages.Invalid)]
    public string Title { get; set; } = "";

    [RegularExpression(@"^(/|https://)\S{0,290}$", ErrorMessage = Messages.Invalid)]
    public string? LinkUrl { get; set; }

    [Range(0, 1000, ErrorMessage = Messages.Invalid)]
    public int SortOrder { get; set; }

    public DateTime? StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed record SettingDto(string Key, string Value, string ValueType, string Group, string Description, decimal? Min, decimal? Max);

public sealed class SaveSettingsRequest
{
    [Required(ErrorMessage = Messages.Required)]
    public Dictionary<string, string> Values { get; set; } = [];
}

public sealed class ContactRequest
{
    [Required(ErrorMessage = Messages.Required), RegularExpression(ValidationPatterns.Name, ErrorMessage = Messages.Invalid)]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), EmailAddress(ErrorMessage = Messages.Invalid), StringLength(150)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), StringLength(100, MinimumLength = 3, ErrorMessage = Messages.Invalid)]
    public string Subject { get; set; } = "";

    [Required(ErrorMessage = Messages.Required), StringLength(2000, MinimumLength = 10, ErrorMessage = Messages.Invalid)]
    public string Message { get; set; } = "";

    /// <summary>A hidden field that only a script fills; a message with it is dropped silently (FR-23.2).</summary>
    public string? Website { get; set; }
}

public sealed record ContactMessageDto(Guid Id, string Name, string Email, string Subject, string Message, string Status, DateTime CreatedAt);

public sealed record StaticPageDto(string Slug, string Title, string Html);

public sealed class SavePageRequest
{
    [Required(ErrorMessage = Messages.Required), StringLength(4000, ErrorMessage = Messages.Invalid)]
    public string Html { get; set; } = "";
}

public sealed record AdminDashboardDto(
    int OrdersToday, decimal SalesToday, int NewUsersToday, int Users, int Sellers, int PendingApplications, int PendingProducts,
    int OpenReturns, int OpenPayouts, IReadOnlyList<ReportPointDto> SalesChart);

public sealed record ReportPointDto(string Label, decimal Value, int Count);

public sealed record SalesReportDto(
    DateOnly From, DateOnly To, string GroupBy, int Orders, decimal GrossSales, decimal Commission, int Delivered, int Cancelled,
    IReadOnlyList<ReportPointDto> Series, IReadOnlyList<ReportPointDto> TopProducts, IReadOnlyList<ReportPointDto> TopSellers);

public sealed record FinanceReportDto(
    decimal Received, decimal Refunds, decimal Commission, decimal PayoutsPaid, decimal SellerBalances, decimal StoreCreditHeld,
    decimal OrderMoneyHeld, decimal PlatformBalance);

public sealed record UserReportDto(int NewUsers, int Buyers, int Sellers, IReadOnlyList<ReportPointDto> Series);

public sealed record PaymentRowDto(Guid Id, string? TransactionId, string Method, decimal Amount, string Status, string? GatewayStatus, DateTime CreatedAt, DateTime? PaidAt, DateTime? LastCheckedAt, string Orders, bool NeedsAttention);

public sealed record LedgerRowDto(DateTime At, Guid TransactionId, string Account, string Kind, decimal Amount, string? OrderNumber, string? ShopName, string? UserName, string? Note);
