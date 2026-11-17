using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Order
{
    public Guid Id { get; set; }

    public string OrderNumber { get; set; } = null!;

    public Guid CheckoutGroupId { get; set; }

    public Guid BuyerId { get; set; }

    public Guid ShopId { get; set; }

    public Guid? CouponId { get; set; }

    public string Status { get; set; } = null!;

    public decimal ItemsTotal { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal CreditUsed { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal CommissionPercent { get; set; }

    public decimal CommissionAmount { get; set; }

    public string ShipName { get; set; } = null!;

    public string ShipPhone { get; set; } = null!;

    public string ShipAddress { get; set; } = null!;

    public Guid ShipAreaId { get; set; }

    public string? CourierName { get; set; }

    public string? TrackingNote { get; set; }

    public DateTime? DeliveredAt { get; set; }

    public bool IsDeleted { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User Buyer { get; set; } = null!;

    public virtual CheckoutGroup CheckoutGroup { get; set; } = null!;

    public virtual Coupon? Coupon { get; set; }

    public virtual ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    public virtual ICollection<OrderStatusHistory> OrderStatusHistories { get; set; } = new List<OrderStatusHistory>();

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();

    public virtual ICollection<ReturnRequest> ReturnRequests { get; set; } = new List<ReturnRequest>();

    public virtual Location ShipArea { get; set; } = null!;

    public virtual Shop Shop { get; set; } = null!;
}
