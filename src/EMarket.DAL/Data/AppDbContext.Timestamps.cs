using Microsoft.EntityFrameworkCore;

namespace EMarket.DAL.Data;

public partial class AppDbContext
{
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // The schema has no trigger for UpdatedAt, so the DAL sets both times in UTC (DB-8).
    private void StampTimes()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Metadata.FindProperty("CreatedAt") is not null
                && (DateTime)entry.Property("CreatedAt").CurrentValue! == default)
            {
                entry.Property("CreatedAt").CurrentValue = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified && entry.Metadata.FindProperty("UpdatedAt") is not null)
            {
                entry.Property("UpdatedAt").CurrentValue = now;
            }
        }
    }
}
