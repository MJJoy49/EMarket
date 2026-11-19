using EMarket.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EMarket.DAL.Data;

public partial class AppDbContext
{
    // DB-10: soft-deleted rows are hidden from every normal query; use IgnoreQueryFilters() to see them.
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasQueryFilter(u => !u.IsDeleted);
        modelBuilder.Entity<Shop>().HasQueryFilter(s => !s.IsDeleted);
        modelBuilder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
        modelBuilder.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);
    }
}
