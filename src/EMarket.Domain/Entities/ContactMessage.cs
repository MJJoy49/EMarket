using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class ContactMessage
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public string Message { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? IpAddress { get; set; }

    public Guid? HandledById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User? HandledBy { get; set; }
}
