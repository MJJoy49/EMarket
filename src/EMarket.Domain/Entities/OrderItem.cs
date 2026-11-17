using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class OrderItem
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public Guid ProductId { get; set; }

    public Guid? VariantId { get; set; }

    public string ProductName { get; set; } = null!;

    public string? VariantText { get; set; }

    public string? Sku { get; set; }

    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public int ReturnedQuantity { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Order Order { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<ReturnItem> ReturnItems { get; set; } = new List<ReturnItem>();

    public virtual Review? Review { get; set; }

    public virtual ProductVariant? Variant { get; set; }
}
