using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class ReturnItem
{
    public Guid Id { get; set; }

    public Guid ReturnRequestId { get; set; }

    public Guid OrderItemId { get; set; }

    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual OrderItem OrderItem { get; set; } = null!;

    public virtual ReturnRequest ReturnRequest { get; set; } = null!;
}
