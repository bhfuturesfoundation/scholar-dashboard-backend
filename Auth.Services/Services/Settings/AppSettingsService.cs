using Auth.Models.Data;
using Auth.Models.DTOs.Settings;
using Auth.Models.Entities.Settings;
using Auth.Models.Enums.Operations;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Auth.Services.Services.Settings
{
    /// <inheritdoc cref="IAppSettingsService"/>
    public class AppSettingsService : IAppSettingsService
    {
        private sealed record Definition(string Key, string Default, Func<string, string?> Normalize);

        /// <summary>
        /// The only keys that can be saved from the app. Defaults match what each service
        /// falls back to, so "Default" on screen is what actually happens. Each normaliser
        /// returns the canonical value, or null when the input is invalid.
        /// </summary>
        private static readonly IReadOnlyList<Definition> Definitions = new List<Definition>
        {
            new("JOURNAL_WINDOW_CLOSE_DAY", "9", v => IntIn(v, 1, 28)),
            new("JOURNAL_REMINDER_DAYS", "5,2,0", v => IntList(v, 0, 27)),
            new("JOURNAL_ENFORCE_WINDOW", "false", Bool),
            new("DIGEST_DAY", "Monday", v => Enum.TryParse<DayOfWeek>(v, true, out var day) ? day.ToString() : null),
            new("DIGEST_HOUR_UTC", "7", v => IntIn(v, 0, 23)),
            new("BACKUP_SCHEDULE_ENABLED", "true", Bool),
            new("BACKUP_SCHEDULE_DAYS", "1,15", v => IntList(v, 1, 28)),
            new("BACKUP_SCHEDULE_HOUR_UTC", "2", v => IntIn(v, 0, 23)),
            new("BACKUP_RETENTION_DAYS", "90", v => IntIn(v, 1, 3650)),
            new("BACKUP_SCHEDULE_FORMAT", "Json",
                v => Enum.TryParse<BackupFormat>(v, true, out var format) && Enum.IsDefined(format) ? format.ToString() : null),
        };

        private readonly ApplicationDbContext _context;
        private readonly AppSettingsConfigurationProvider _provider;
        private readonly IConfiguration _configuration;
        private readonly IAuditService _auditService;

        public AppSettingsService(
            ApplicationDbContext context,
            AppSettingsConfigurationProvider provider,
            IConfiguration configuration,
            IAuditService auditService)
        {
            _context = context;
            _provider = provider;
            _configuration = configuration;
            _auditService = auditService;
        }

        public async Task<List<AppSettingDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var saved = await _context.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, cancellationToken);

            return Definitions.Select(definition =>
            {
                if (saved.TryGetValue(definition.Key, out var row))
                {
                    return new AppSettingDto
                    {
                        Key = definition.Key,
                        Value = row.Value,
                        Source = "app",
                        DefaultValue = definition.Default,
                        UpdatedAt = row.UpdatedAt
                    };
                }

                // No saved row, so configuration holds the environment variable, if any.
                var server = _configuration[definition.Key];
                var fromServer = !string.IsNullOrWhiteSpace(server) && definition.Normalize(server) is not null;

                return new AppSettingDto
                {
                    Key = definition.Key,
                    Value = fromServer ? definition.Normalize(server!)! : definition.Default,
                    Source = fromServer ? "server" : "default",
                    DefaultValue = definition.Default
                };
            }).ToList();
        }

        public async Task<List<AppSettingDto>> UpdateAsync(
            string adminId, UpdateAppSettingsRequest request, CancellationToken cancellationToken = default)
        {
            var definitions = Definitions.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
            var changes = new List<string>();

            foreach (var (key, rawValue) in request.Values)
            {
                if (!definitions.TryGetValue(key, out var definition))
                {
                    throw new ValidationException($"'{key}' cannot be changed from the app.");
                }

                var row = await _context.AppSettings.FirstOrDefaultAsync(s => s.Key == definition.Key, cancellationToken);

                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    if (row is null) continue;
                    _context.AppSettings.Remove(row);
                    changes.Add($"{definition.Key}=<server>");
                    continue;
                }

                var value = definition.Normalize(rawValue.Trim())
                    ?? throw new ValidationException($"'{rawValue}' is not a valid value for {definition.Key}.");

                if (row is null)
                {
                    row = new AppSetting { Key = definition.Key };
                    _context.AppSettings.Add(row);
                }
                else if (row.Value == value)
                {
                    continue;
                }

                row.Value = value;
                row.UpdatedAt = DateTime.UtcNow;
                row.UpdatedByUserId = adminId;
                changes.Add($"{definition.Key}={value}");
            }

            await _context.SaveChangesAsync(cancellationToken);
            await LoadAsync(cancellationToken);

            if (changes.Count > 0)
            {
                await _auditService.LogAsync("Settings.Updated", adminId, string.Join("; ", changes));
            }

            return await GetAllAsync(cancellationToken);
        }

        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            var saved = await _context.AppSettings
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

            _provider.Replace(saved);
        }

        private static string? IntIn(string value, int min, int max) =>
            int.TryParse(value, out var number) && number >= min && number <= max ? number.ToString() : null;

        private static string? IntList(string value, int min, int max)
        {
            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var numbers = new List<int>();

            foreach (var part in parts)
            {
                if (!int.TryParse(part, out var number) || number < min || number > max) return null;
                numbers.Add(number);
            }

            return numbers.Count == 0 ? null : string.Join(",", numbers.Distinct());
        }

        private static string? Bool(string value) =>
            bool.TryParse(value, out var flag) ? flag.ToString().ToLowerInvariant() : null;
    }
}
