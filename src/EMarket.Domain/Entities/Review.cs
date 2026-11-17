using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Review
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid OrderItemId { get; set; }

    public Guid BuyerId { get; set; }

    public int Rating { get; set; }

    public string? Comment { get; set; }

    public string Status { get; set; } = null!;

    public string? SellerReply { get; set; }

    public DateTime? SellerRepliedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User Buyer { get; set; } = null!;

    public virtual OrderItem OrderItem { get; set; } = null!;

    public virtual Product Product { get; set; } = null!;

    public virtual ICollection<ReviewReport> ReviewReports { get; set; } = new List<ReviewReport>();
}
