using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Coupon
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string DiscountType { get; set; } = null!;

    public decimal DiscountValue { get; set; }

    public decimal? MaxDiscount { get; set; }

    public decimal? MinOrderAmount { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public int? UsageLimit { get; set; }

    public int PerUserLimit { get; set; }

    public int UsedCount { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Cart> Carts { get; set; } = new List<Cart>();

    public virtual ICollection<CouponRedemption> CouponRedemptions { get; set; } = new List<CouponRedemption>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
