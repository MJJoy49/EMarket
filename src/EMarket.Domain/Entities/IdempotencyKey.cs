using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class IdempotencyKey
{
    public Guid Id { get; set; }

    public string Key { get; set; } = null!;

    public Guid UserId { get; set; }

    public string Endpoint { get; set; } = null!;

    public string RequestHash { get; set; } = null!;

    public int? ResponseStatus { get; set; }

    public string? ResponseBody { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
