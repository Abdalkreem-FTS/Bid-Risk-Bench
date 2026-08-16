using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BidRisk.Auction.Data;

public sealed class DatabaseHealthCheck(AuctionDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("cannot reach the database");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("cannot reach the database", exception);
        }
    }
}
