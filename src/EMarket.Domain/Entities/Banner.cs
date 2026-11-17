using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Banner
{
    public Guid Id { get; set; }

    public string Title { get; set; } = null!;

    public string ImageUrl { get; set; } = null!;

    public string? LinkUrl { get; set; }

    public int SortOrder { get; set; }

    public DateTime? StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
