using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Shop
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? LogoUrl { get; set; }

    public string? BannerUrl { get; set; }

    public string Status { get; set; } = null!;

    public decimal? CommissionPercent { get; set; }

    public decimal? InsideDhakaFee { get; set; }

    public decimal? OutsideDhakaFee { get; set; }

    public decimal? FreeDeliveryAbove { get; set; }

    public string PayoutMethod { get; set; } = null!;

    public string PayoutAccount { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual User Owner { get; set; } = null!;

    public virtual ICollection<Payout> Payouts { get; set; } = new List<Payout>();

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
