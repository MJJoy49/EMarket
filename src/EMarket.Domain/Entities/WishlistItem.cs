using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class WishlistItem
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ProductId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Product Product { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
