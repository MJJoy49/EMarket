using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class CheckoutGroup
{
    public Guid Id { get; set; }

    public Guid BuyerId { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public decimal ItemsTotal { get; set; }

    public decimal DeliveryTotal { get; set; }

    public decimal DiscountTotal { get; set; }

    public decimal CreditUsed { get; set; }

    public decimal PayableTotal { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User Buyer { get; set; } = null!;

    public virtual CouponRedemption? CouponRedemption { get; set; }

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
