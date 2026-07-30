using Auth.Models.Data;
using Auth.Models.DTOs.Operations;
using Auth.Services.Interfaces.Email;
using Auth.Services.Interfaces.Operations;
using Auth.Services.Interfaces.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;

namespace Auth.Services.Services.Operations
{
    /// <summary>
    /// Read-only view of how the deployment is doing: database, migrations, integrations,
    /// and which environment variables are set.
    ///
    /// Every check is defensive — a failing check must report "unhealthy", never throw. This
    /// screen is what someone opens when the app is already misbehaving, so it has to work
    /// when things are broken.
    /// </summary>
    public class OperationsService : IOperationsService
    {
        /// <summary>
        /// Process start time. Static because it's a property of the process, not of any
        /// scoped instance of this service.
        /// </summary>
        private static readonly DateTime ProcessStartedUtc = DateTime.UtcNow;

        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailDispatcher _emailDispatcher;
        private readonly IDropboxStorage _dropbox;
        private readonly ILogger<OperationsService> _logger;

        public OperationsService(
            ApplicationDbContext context,
            IConfiguration configuration,
            IEmailDispatcher emailDispatcher,
            IDropboxStorage dropbox,
            ILogger<OperationsService> logger)
        {
            _context = context;
            _configuration = configuration;
            _emailDispatcher = emailDispatcher;
            _dropbox = dropbox;
            _logger = logger;
        }

        public async Task<DeployHealthDto> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            var checks = new List<HealthCheckDto>
            {
                await CheckDatabaseAsync(cancellationToken),
                await CheckMigrationsAsync(cancellationToken),
                CheckEmailProviders(),
                CheckDropbox(),
                CheckRedis(),
                CheckMessageBroker(),
                CheckSandboxMode()
            };

            var health = new DeployHealthDto
            {
                Environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Production",
                StartedAtUtc = ProcessStartedUtc,
                Uptime = DateTime.UtcNow - ProcessStartedUtc,
                AppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),

                // Railway exposes these; other platforms simply won't have them.
                DeploymentId = _configuration["RAILWAY_DEPLOYMENT_ID"]
                    ?? _configuration["RAILWAY_GIT_COMMIT_SHA"],

                Checks = checks
            };

            // NotConfigured is a deliberate choice, not a fault, so it must not drag the
            // overall state down — otherwise the header is permanently amber for anyone who
            // hasn't wired up every optional integration.
            health.OverallState = checks
                .Where(c => c.State != HealthState.NotConfigured)
                .Select(c => c.State)
                .DefaultIfEmpty(HealthState.Healthy)
                .Max();

            return health;
        }

        public EnvironmentStatusDto GetEnvironmentStatus()
        {
            var variables = EnvironmentManifest.All
                .Select(definition =>
                {
                    // Presence only. The value is tested for emptiness and immediately
                    // discarded — it is never stored, logged or returned.
                    var isSet = !string.IsNullOrWhiteSpace(_configuration[definition.Name]);

                    return new EnvVarStatusDto
                    {
                        Name = definition.Name,
                        Category = definition.Category,
                        Importance = definition.Importance.ToString(),
                        IsSet = isSet,
                        Purpose = definition.Purpose,
                        ConsequenceIfMissing = isSet ? null : definition.ConsequenceIfMissing
                    };
                })
                .OrderBy(v => v.Category)
                .ThenBy(v => v.Importance)
                .ThenBy(v => v.Name)
                .ToList();

            return new EnvironmentStatusDto
            {
                TotalTracked = variables.Count,
                SetCount = variables.Count(v => v.IsSet),
                MissingRequiredCount = variables.Count(v => !v.IsSet && v.Importance == "Required"),
                MissingRecommendedCount = variables.Count(v => !v.IsSet && v.Importance == "Recommended"),
                Variables = variables
            };
        }

        // ── Individual checks ─────────────────────────────────────────────────

        private async Task<HealthCheckDto> CheckDatabaseAsync(CancellationToken cancellationToken)
        {
            var check = new HealthCheckDto { Name = "Database", Category = "Core" };

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
                stopwatch.Stop();

                if (!canConnect)
                {
                    check.State = HealthState.Unhealthy;
                    check.Summary = "Cannot connect.";
                    check.Remediation = "Check PGHOST, PGDATABASE, PGUSER and PGPASSWORD, and that the database is running.";
                    return check;
                }

                check.Details["latencyMs"] = stopwatch.ElapsedMilliseconds.ToString();

                // A reachable but slow database is the shape of an incident starting.
                if (stopwatch.ElapsedMilliseconds > 1000)
                {
                    check.State = HealthState.Degraded;
                    check.Summary = $"Connected, but slowly ({stopwatch.ElapsedMilliseconds} ms).";
                    check.Remediation = "Check database load and whether the app and database are in the same region.";
                    return check;
                }

                check.State = HealthState.Healthy;
                check.Summary = $"Connected in {stopwatch.ElapsedMilliseconds} ms.";
                return check;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed.");
                check.State = HealthState.Unhealthy;
                check.Summary = "Connection failed.";
                check.Details["error"] = ex.GetType().Name;
                check.Remediation = "Check the database credentials and that the instance is reachable.";
                return check;
            }
        }

        private async Task<HealthCheckDto> CheckMigrationsAsync(CancellationToken cancellationToken)
        {
            var check = new HealthCheckDto { Name = "Migrations", Category = "Core" };

            try
            {
                var applied = (await _context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
                var pending = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

                check.Details["applied"] = applied.Count.ToString();
                check.Details["pending"] = pending.Count.ToString();

                if (applied.Count > 0)
                    check.Details["latest"] = applied[^1];

                if (pending.Count > 0)
                {
                    // Migrations apply on startup, so pending ones here mean the last start
                    // didn't complete them — worth flagging rather than hiding.
                    check.State = HealthState.Degraded;
                    check.Summary = $"{pending.Count} migration(s) pending.";
                    check.Details["pendingNames"] = string.Join(", ", pending.Take(5));
                    check.Remediation = "Restart the service to apply them, or check the startup logs for a migration failure.";
                    return check;
                }

                check.State = HealthState.Healthy;
                check.Summary = $"Up to date — {applied.Count} applied.";
                return check;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Migration health check failed.");
                check.State = HealthState.Unhealthy;
                check.Summary = "Could not read migration history.";
                check.Remediation = "The database may be unreachable or the migrations table missing.";
                return check;
            }
        }

        private HealthCheckDto CheckEmailProviders()
        {
            var check = new HealthCheckDto { Name = "Email delivery", Category = "Integrations" };

            try
            {
                var providers = _emailDispatcher.GetProviders();
                var configured = providers.Where(p => p.IsConfigured && p.Key != "log").ToList();

                check.Details["configured"] = string.Join(", ", configured.Select(p => p.Key));
                check.Details["default"] = _emailDispatcher.DefaultProviderKey ?? "none";

                if (configured.Count == 0)
                {
                    check.State = HealthState.Unhealthy;
                    check.Summary = "No delivery provider configured.";
                    check.Remediation = "Configure at least one of SMTP, GMass, Mailchimp, Resend or EmailJS. See docs/EMAIL_PROVIDERS.md.";
                    return check;
                }

                // One provider works, but there's nothing to fall back to if it fails.
                if (configured.Count == 1)
                {
                    check.State = HealthState.Degraded;
                    check.Summary = $"Only {configured[0].DisplayName} is configured.";
                    check.Remediation = "Configure a second provider and enable EMAIL_ENABLE_FALLBACK so one outage doesn't stop all mail.";
                    return check;
                }

                check.State = HealthState.Healthy;
                check.Summary = $"{configured.Count} providers configured.";
                return check;
            }
            catch (Exception ex)
            {
                check.State = HealthState.Unhealthy;
                check.Summary = "Could not read provider configuration.";
                check.Details["error"] = ex.GetType().Name;
                return check;
            }
        }

        private HealthCheckDto CheckDropbox()
        {
            var check = new HealthCheckDto { Name = "Dropbox", Category = "Integrations" };

            if (!_dropbox.IsConfigured)
            {
                check.State = HealthState.NotConfigured;
                check.Summary = "Not configured.";
                check.Remediation = _dropbox.ConfigurationHint;
                return check;
            }

            // Deliberately not making a live API call: this endpoint is polled, and burning a
            // token exchange per poll would be wasteful. Configuration presence is what the
            // screen needs; a real failure surfaces in the backup history.
            check.State = HealthState.Healthy;
            check.Summary = "Configured (OAuth2 refresh flow).";
            return check;
        }

        private HealthCheckDto CheckRedis()
        {
            var check = new HealthCheckDto { Name = "Redis (SignalR backplane)", Category = "Infrastructure" };

            var configured = !string.IsNullOrWhiteSpace(_configuration["REDIS_URL"])
                || !string.IsNullOrWhiteSpace(_configuration["REDIS_CONNECTION_STRING"]);

            if (!configured)
            {
                check.State = HealthState.NotConfigured;
                check.Summary = "Not configured — hubs run in-memory.";
                check.Remediation = "Fine on a single instance. Required for real-time features once you scale beyond one.";
                return check;
            }

            check.State = HealthState.Healthy;
            check.Summary = "Configured.";
            return check;
        }

        private HealthCheckDto CheckMessageBroker()
        {
            var check = new HealthCheckDto { Name = "Message broker", Category = "Infrastructure" };

            var configured = !string.IsNullOrWhiteSpace(_configuration["RABBITMQ_URL"])
                || !string.IsNullOrWhiteSpace(_configuration["RABBITMQ_HOST"]);

            if (!configured)
            {
                check.State = HealthState.Degraded;
                check.Summary = "Not configured — queued email uses the no-op broker.";
                check.Remediation = "Queued messages are silently dropped. Configure RABBITMQ_URL, or confirm nothing relies on the queue.";
                return check;
            }

            check.State = HealthState.Healthy;
            check.Summary = "Configured.";
            return check;
        }

        private HealthCheckDto CheckSandboxMode()
        {
            var check = new HealthCheckDto { Name = "Email sandbox", Category = "Integrations" };

            var redirect = _configuration["EMAIL_SANDBOX_REDIRECT_TO"];

            if (string.IsNullOrWhiteSpace(redirect))
            {
                check.State = HealthState.Healthy;
                check.Summary = "Off — mail goes to real recipients.";
                return check;
            }

            // Loud on purpose. Sandbox left on in production means every campaign silently
            // reaches nobody, and the campaign history reports success the whole time.
            check.State = HealthState.Degraded;
            check.Summary = "ON — every outgoing email is being redirected.";
            check.Remediation = "Unset EMAIL_SANDBOX_REDIRECT_TO in production, or no recipient will receive anything.";
            return check;
        }
    }
}
