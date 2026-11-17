using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class ReviewReport
{
    public Guid Id { get; set; }

    public Guid ReviewId { get; set; }

    public Guid ReporterId { get; set; }

    public string Reason { get; set; } = null!;

    public string Status { get; set; } = null!;

    public Guid? DecidedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? DecidedBy { get; set; }

    public virtual User Reporter { get; set; } = null!;

    public virtual Review Review { get; set; } = null!;
}
