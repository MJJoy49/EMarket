using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Payment
{
    public Guid Id { get; set; }

    public Guid CheckoutGroupId { get; set; }

    public Guid BuyerId { get; set; }

    public string Method { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Status { get; set; } = null!;

    public string? TransactionId { get; set; }

    public string? GatewayStatus { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User Buyer { get; set; } = null!;

    public virtual CheckoutGroup CheckoutGroup { get; set; } = null!;

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
