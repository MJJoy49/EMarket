using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class ReturnRequest
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid BuyerId { get; set; }

    public string Reason { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string? PhotoPaths { get; set; }

    public string Status { get; set; } = null!;

    public string? RejectionReason { get; set; }

    public DateTime? EscalatedAt { get; set; }

    public string? RefundMethod { get; set; }

    public Guid? DecidedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User Buyer { get; set; } = null!;

    public virtual User? DecidedBy { get; set; }

    public virtual Order Order { get; set; } = null!;

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();

    public virtual ICollection<ReturnItem> ReturnItems { get; set; } = new List<ReturnItem>();
}
