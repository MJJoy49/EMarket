using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Address
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string FullName { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public Guid DivisionId { get; set; }

    public Guid DistrictId { get; set; }

    public Guid AreaId { get; set; }

    public string AddressLine { get; set; } = null!;

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Location Area { get; set; } = null!;

    public virtual Location District { get; set; } = null!;

    public virtual Location Division { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
