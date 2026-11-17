using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Product
{
    public Guid Id { get; set; }

    public Guid ShopId { get; set; }

    public Guid CategoryId { get; set; }

    public string Name { get; set; } = null!;

    public string? Brand { get; set; }

    public string Description { get; set; } = null!;

    public decimal Price { get; set; }

    public decimal DiscountPercent { get; set; }

    public int Stock { get; set; }

    public string Status { get; set; } = null!;

    public string? RejectionReason { get; set; }

    public decimal AverageRating { get; set; }

    public int ReviewCount { get; set; }

    public bool IsDeleted { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

    public virtual Category Category { get; set; } = null!;

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();

    public virtual ICollection<ProductImage> ProductImages { get; set; } = new List<ProductImage>();

    public virtual ICollection<ProductVariant> ProductVariants { get; set; } = new List<ProductVariant>();

    public virtual ICollection<Review> Reviews { get; set; } = new List<Review>();

    public virtual Shop Shop { get; set; } = null!;

    public virtual ICollection<WishlistItem> WishlistItems { get; set; } = new List<WishlistItem>();
}
