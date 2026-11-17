using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class CouponRedemption
{
    public Guid Id { get; set; }

    public Guid CouponId { get; set; }

    public Guid UserId { get; set; }

    public Guid CheckoutGroupId { get; set; }

    public decimal DiscountAmount { get; set; }

    public DateTime? ReleasedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual CheckoutGroup CheckoutGroup { get; set; } = null!;

    public virtual Coupon Coupon { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
