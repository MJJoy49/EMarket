using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class SellerApplication
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string ShopName { get; set; } = null!;

    public string ShopDescription { get; set; } = null!;

    public string BusinessType { get; set; } = null!;

    public string NidNumber { get; set; } = null!;

    public string NidFrontPath { get; set; } = null!;

    public string NidBackPath { get; set; } = null!;

    public string? TradeLicencePath { get; set; }

    public string PayoutMethod { get; set; } = null!;

    public string PayoutAccount { get; set; } = null!;

    public string Status { get; set; } = null!;

    public Guid? ReviewedById { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? ReviewedBy { get; set; }

    public virtual User User { get; set; } = null!;
}
