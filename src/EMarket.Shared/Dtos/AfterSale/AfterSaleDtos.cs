using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.AfterSale;

public sealed class ReturnLineInput
{
    [Required(ErrorMessage = Messages.Required)]
    public Guid? OrderItemId { get; set; }

    [Range(1, 1000, ErrorMessage = Messages.Invalid)]
    public int Quantity { get; set; } = 1;
}

public sealed class ReturnRequestInput
{
    [MinLength(1, ErrorMessage = Messages.Required)]
    public List<ReturnLineInput> Items { get; set; } = [];

    [Required(ErrorMessage = Messages.Required), RegularExpression("^(Damaged|WrongItem|NotAsDescribed|NotReceived|Other)$", ErrorMessage = Messages.Invalid)]
    public string Reason { get; set; } = "";

    [StringLength(500, ErrorMessage = Messages.Invalid)]
    public string? Description { get; set; }

    [RegularExpression("^(OriginalMethod|StoreCredit)$", ErrorMessage = Messages.Invalid)]
    public string RefundMethod { get; set; } = "OriginalMethod";
}

public sealed record ReturnLineDto(Guid OrderItemId, string ProductName, string? VariantText, int Quantity, decimal UnitPrice);

public sealed record ReturnDto(
    Guid Id, Guid OrderId, string OrderNumber, string BuyerName, string ShopName, string Reason, string Description, string Status,
    string? RejectionReason, string? RefundMethod, bool Escalated, decimal RefundAmount, DateTime CreatedAt, IReadOnlyList<ReturnLineDto> Items,
    IReadOnlyList<string> PhotoUrls, IReadOnlyList<string> AllowedActions);

public sealed class DecisionRequest
{
    public bool Approve { get; set; }

    [StringLength(500, MinimumLength = 10, ErrorMessage = Messages.Invalid)]
    public string? Reason { get; set; }
}

public sealed class ItemReceivedRequest
{
    /// <summary>FR-13.11: when the goods can be sold again, the stock goes back up.</summary>
    public bool Resellable { get; set; }
}

public sealed class ReviewInput
{
    [Range(1, 5, ErrorMessage = Messages.Required)]
    public int Rating { get; set; }

    [StringLength(1000, ErrorMessage = Messages.Invalid)]
    public string? Comment { get; set; }
}

public sealed record ReviewDto(
    Guid Id, Guid ProductId, string ProductName, string ReviewerName, int Rating, string? Comment, DateTime CreatedAt, bool VerifiedPurchase,
    string Status, string? SellerReply, DateTime? SellerRepliedAt, int OpenReports);

public sealed class ReplyInput
{
    [Required(ErrorMessage = Messages.Required), StringLength(500, MinimumLength = 1, ErrorMessage = Messages.Invalid)]
    public string Reply { get; set; } = "";
}

public sealed class ReportInput
{
    [Required(ErrorMessage = Messages.Required), StringLength(300, MinimumLength = 5, ErrorMessage = Messages.Invalid)]
    public string Reason { get; set; } = "";
}

public sealed record NotificationDto(Guid Id, string Type, string Title, string Message, string? LinkUrl, bool IsRead, DateTime CreatedAt);
