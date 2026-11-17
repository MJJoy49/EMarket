using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class AuditLog
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public string Action { get; set; } = null!;

    public string EntityName { get; set; } = null!;

    public Guid? EntityId { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public string? CorrelationId { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? User { get; set; }
}
