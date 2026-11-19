using EMarket.DAL.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EMarket.Api.Infrastructure;

/// <summary>The ready endpoint fails with 503 when the database cannot be reached (NFR-14).</summary>
public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database is not reachable.");
}
