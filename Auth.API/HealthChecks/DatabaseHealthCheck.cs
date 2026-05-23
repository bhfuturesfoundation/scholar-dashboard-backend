using Auth.Models.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auth.API.HealthChecks
{
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly ApplicationDbContext _context;

        public DatabaseHealthCheck(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy("Database is reachable.")
                    : HealthCheckResult.Unhealthy("Database connection check returned false.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("Database is unreachable.", ex);
            }
        }
    }
}
