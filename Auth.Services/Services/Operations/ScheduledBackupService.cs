using Auth.Models.Enums.Operations;
using Auth.Services.Interfaces.Operations;
using Auth.Services.Interfaces.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Operations
{
    /// <summary>
    /// Runs a database backup twice a month and prunes expired ones.
    ///
    /// Deliberately built on "has a backup run today on a scheduled day" rather than an
    /// in-memory timer. A timer resets on every deploy, and this app deploys often — a
    /// fortnightly timer would realistically never fire. Checking the persisted backup
    /// history instead means the schedule survives restarts, and a container that was down
    /// on the 1st still takes its backup when it comes back up.
    ///
    /// It also means multiple instances converge safely: whichever wakes first records the
    /// backup, and the others see it and skip.
    /// </summary>
    public class ScheduledBackupService : BackgroundService
    {
        /// <summary>
        /// How often to check whether a backup is due. Hourly is far more often than the
        /// schedule fires, but it is a single indexed query and it means a service that was
        /// down at the scheduled hour catches up within the hour rather than waiting a
        /// fortnight.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ScheduledBackupService> _logger;

        public ScheduledBackupService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<ScheduledBackupService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        private bool Enabled =>
            !string.Equals(_configuration["BACKUP_SCHEDULE_ENABLED"]?.Trim(), "false",
                StringComparison.OrdinalIgnoreCase);

        /// <summary>Days of the month a backup is taken. Default 1 and 15 — twice monthly.</summary>
        private int[] ScheduledDays
        {
            get
            {
                var raw = _configuration["BACKUP_SCHEDULE_DAYS"];
                if (string.IsNullOrWhiteSpace(raw)) return new[] { 1, 15 };

                var days = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(d => int.TryParse(d, out var value) ? value : 0)
                    .Where(d => d is >= 1 and <= 28)   // 28 so every month has the day
                    .Distinct()
                    .ToArray();

                return days.Length > 0 ? days : new[] { 1, 15 };
            }
        }

        /// <summary>Hour (UTC) the backup runs on a scheduled day. Default 02:00.</summary>
        private int ScheduledHourUtc =>
            int.TryParse(_configuration["BACKUP_SCHEDULE_HOUR_UTC"], out var hour) && hour is >= 0 and <= 23
                ? hour
                : 2;

        private int RetentionDays =>
            int.TryParse(_configuration["BACKUP_RETENTION_DAYS"], out var days) && days > 0
                ? days
                : 90;

        private BackupFormat Format =>
            Enum.TryParse<BackupFormat>(_configuration["BACKUP_SCHEDULE_FORMAT"], ignoreCase: true, out var format)
                ? format
                : BackupFormat.Json;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled)
            {
                _logger.LogInformation("Scheduled backups are disabled (BACKUP_SCHEDULE_ENABLED=false).");
                return;
            }

            _logger.LogInformation(
                "Scheduled backups active: days {Days} at {Hour:00}:00 UTC, format {Format}, retention {Retention} days.",
                string.Join(" and ", ScheduledDays), ScheduledHourUtc, Format, RetentionDays);

            // Let migrations and seeding finish before touching the database.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A BackgroundService that throws is torn down permanently and silently.
                    // Swallowing keeps the schedule alive across transient failures.
                    _logger.LogError(ex, "Scheduled backup tick failed. Continuing.");
                }

                try { await Task.Delay(PollInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task TickAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;

            if (!ScheduledDays.Contains(now.Day)) return;
            if (now.Hour < ScheduledHourUtc) return;

            using var scope = _scopeFactory.CreateScope();
            var backups = scope.ServiceProvider.GetRequiredService<IBackupService>();

            var history = await backups.GetHistoryAsync(20, cancellationToken);

            // Already taken today by this instance or another one.
            var alreadyToday = history.Any(b =>
                b.IsAutomatic &&
                b.Status == BackupStatus.Completed &&
                b.StartedAt.Date == now.Date);

            if (alreadyToday) return;

            _logger.LogInformation("Scheduled backup starting for {Date:yyyy-MM-dd}.", now);

            try
            {
                var artifact = await backups.CreateAsync(
                    new CreateBackupRequest
                    {
                        Format = Format,

                        // Never credentials on an unattended backup. An automated job produces
                        // files nobody is watching; a password-hash-bearing artefact should
                        // only ever exist because a person deliberately asked for one.
                        IncludeSensitiveData = false,

                        ArchiveToDropbox = true,
                        RetentionDays = RetentionDays
                    },
                    userId: null,
                    userName: "Scheduled backup",
                    cancellationToken);

                var dropbox = scope.ServiceProvider.GetRequiredService<IDropboxStorage>();

                if (!artifact.Record.IsArchived)
                {
                    // Loud, because an unarchived scheduled backup is effectively no backup:
                    // the bytes were returned to nobody and the container's disk is wiped on
                    // the next deploy.
                    _logger.LogError(
                        "Scheduled backup {File} was produced but NOT archived — it exists nowhere durable. {Hint}",
                        artifact.Record.FileName,
                        dropbox.IsConfigured ? "Check the Dropbox upload error above." : dropbox.ConfigurationHint);
                }
                else
                {
                    _logger.LogInformation(
                        "Scheduled backup {File} archived to {Path} ({Size} bytes).",
                        artifact.Record.FileName, artifact.Record.StoragePath, artifact.Record.SizeBytes);
                }

                var pruned = await backups.PruneExpiredAsync(cancellationToken);
                if (pruned > 0) _logger.LogInformation("Pruned {Count} expired backup record(s).", pruned);
            }
            catch (Exception ex)
            {
                // The failure is already recorded as a Failed row by BackupService, so the
                // operations console shows it even though nobody watched this run.
                _logger.LogError(ex, "Scheduled backup failed.");
            }
        }
    }
}
