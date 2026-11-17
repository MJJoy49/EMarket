using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Cart
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? CouponId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    public virtual Coupon? Coupon { get; set; }

    public virtual User User { get; set; } = null!;
}
