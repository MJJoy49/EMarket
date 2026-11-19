using System.ComponentModel.DataAnnotations;
using EMarket.Shared.Constants;

namespace EMarket.Shared.Dtos.Sellers;

/// <summary>Text part of the seller application form (UI21). The photos travel as files beside it.</summary>
public sealed class SellerApplicationRequest
{
    [Required(ErrorMessage = Messages.Required), RegularExpression(@"^[\p{L}\d \-]{3,60}$", ErrorMessage = Messages.Invalid)]
    public string ShopName { get; set; } = "";

    [StringLength(500, ErrorMessage = Messages.Invalid)]
    public string? ShopDescription { get; set; }

    [Required(ErrorMessage = Messages.Required), RegularExpression("^(Individual|Business)$", ErrorMessage = Messages.Invalid)]
    public string BusinessType { get; set; } = "Individual";

    [Required(ErrorMessage = Messages.Required), RegularExpression(@"^(\d{10}|\d{13}|\d{17})$", ErrorMessage = Messages.Invalid)]
    public string NidNumber { get; set; } = "";

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

    [Range(typeof(bool), "true", "true", ErrorMessage = Messages.Required)]
    public bool AcceptSellerTerms { get; set; }
}

public sealed record SellerApplicationDto(
    Guid Id, Guid UserId, string ApplicantName, string ApplicantEmail, string ShopName, string ShopDescription,
    string BusinessType, string Status, string PayoutMethod, string? RejectionReason, DateTime SubmittedAt,
    DateTime? ReviewedAt, bool HasTradeLicence, string? NidNumber, string? PayoutAccount);

public sealed class ReviewDecisionRequest
{
    public bool Approve { get; set; }

    [StringLength(500, MinimumLength = 10, ErrorMessage = Messages.Invalid)]
    public string? Reason { get; set; }
}
