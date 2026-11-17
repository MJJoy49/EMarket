using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Payout
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public decimal Amount { get; set; }

    public string Method { get; set; } = null!;

    public string AccountSnapshot { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? ReferenceNo { get; set; }

    public string? RejectionReason { get; set; }

    public Guid? DecidedById { get; set; }

    public DateTime? DecidedAt { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? DecidedBy { get; set; }

    public virtual Shop Shop { get; set; } = null!;
}
