using System;
using System.Collections.Generic;

namespace EMarket.Domain.Entities;

public partial class Location
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public string Level { get; set; } = null!;

    public string Name { get; set; } = null!;

    public bool IsInsideDhaka { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Address> AddressAreas { get; set; } = new List<Address>();

    public virtual ICollection<Address> AddressDistricts { get; set; } = new List<Address>();

    public virtual ICollection<Address> AddressDivisions { get; set; } = new List<Address>();

    public virtual ICollection<Location> InverseParent { get; set; } = new List<Location>();

    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();

    public virtual Location? Parent { get; set; }
}
