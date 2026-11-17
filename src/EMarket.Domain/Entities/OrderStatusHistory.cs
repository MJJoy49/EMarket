using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class OrderStatusHistory
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string? FromStatus { get; set; }

    public string ToStatus { get; set; } = null!;

    public Guid? ChangedById { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? ChangedBy { get; set; }

    public virtual Order Order { get; set; } = null!;
}
