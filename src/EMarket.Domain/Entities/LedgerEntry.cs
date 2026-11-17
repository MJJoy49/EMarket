using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class LedgerEntry
{
    public Guid Id { get; set; }

    public Guid TransactionId { get; set; }

    public string Account { get; set; } = null!;

    public Guid? ShopId { get; set; }

    public Guid? UserId { get; set; }

    public Guid? OrderId { get; set; }

    public Guid? RefId { get; set; }

    public string Kind { get; set; } = null!;

    public decimal Amount { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Order? Order { get; set; }

    public virtual Shop? Shop { get; set; }

    public virtual User? User { get; set; }
}
