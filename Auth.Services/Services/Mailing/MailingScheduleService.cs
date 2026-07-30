using Auth.Models.Data;
using Auth.Models.DTOs.Mailing;
using Auth.Models.Entities.Mailing;
using Auth.Models.Enums.Mailing;
using Auth.Models.Exceptions;
using Auth.Models.Request.Mailing;
using Auth.Services.Interfaces.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Auth.Services.Services.Mailing
{
    /// <summary>CRUD for the schedules that <see cref="MailingSchedulerService"/> executes.</summary>
    public class MailingScheduleService : IMailingScheduleService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMailingCampaignService _campaignService;
        private readonly ILogger<MailingScheduleService> _logger;

        public MailingScheduleService(
            ApplicationDbContext context,
            IMailingCampaignService campaignService,
            ILogger<MailingScheduleService> logger)
        {
            _context = context;
            _campaignService = campaignService;
            _logger = logger;
        }

        public async Task<List<MailingScheduleDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var schedules = await _context.MailingSchedules
                .AsNoTracking()
                .Include(s => s.Template)
                .OrderByDescending(s => s.IsEnabled)
                .ThenBy(s => s.NextRunAt)
                .ToListAsync(cancellationToken);

            var results = new List<MailingScheduleDto>(schedules.Count);

            foreach (var schedule in schedules)
            {
                var dto = Map(schedule);

                // Resolved live rather than stored: the audience is a query, and its size
                // changes as firms are imported or unsubscribed.
                dto.AudienceSize = (await _campaignService.ResolveAudienceAsync(
                    ToSelection(schedule), cancellationToken)).Count;

                results.Add(dto);
            }

            return results;
        }

        public async Task<MailingScheduleDto> CreateAsync(
            UpsertScheduleRequest request, string userId, string userName, CancellationToken cancellationToken = default)
        {
            Validate(request);

            await EnsureTemplateExistsAsync(request.TemplateId, cancellationToken);

            var schedule = new MailingSchedule
            {
                CreatedByUserId = userId,
                CreatedByName = userName
            };

            Apply(schedule, request);

            // Start now unless a later time was given, so an operator who just wants it
            // running doesn't have to pick a date.
            schedule.NextRunAt = request.StartAt ?? DateTime.UtcNow;

            _context.MailingSchedules.Add(schedule);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created mailing schedule {Id} ({Name}).", schedule.Id, schedule.Name);

            return (await GetAllAsync(cancellationToken)).First(s => s.Id == schedule.Id);
        }

        public async Task<MailingScheduleDto> UpdateAsync(
            int id, UpsertScheduleRequest request, CancellationToken cancellationToken = default)
        {
            Validate(request);

            var schedule = await _context.MailingSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException("Schedule", id.ToString());

            await EnsureTemplateExistsAsync(request.TemplateId, cancellationToken);

            var wasDisabled = !schedule.IsEnabled;

            Apply(schedule, request);
            schedule.UpdatedAt = DateTime.UtcNow;

            if (request.StartAt.HasValue)
            {
                schedule.NextRunAt = request.StartAt;
            }
            else if (wasDisabled && request.IsEnabled && schedule.NextRunAt is null)
            {
                // Re-enabling a finished schedule needs a next run, or it stays inert.
                schedule.NextRunAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return (await GetAllAsync(cancellationToken)).First(s => s.Id == id);
        }

        public async Task SetEnabledAsync(int id, bool enabled, CancellationToken cancellationToken = default)
        {
            var schedule = await _context.MailingSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException("Schedule", id.ToString());

            schedule.IsEnabled = enabled;
            schedule.UpdatedAt = DateTime.UtcNow;

            if (enabled && schedule.NextRunAt is null)
                schedule.NextRunAt = DateTime.UtcNow;

            if (enabled) schedule.LastError = null;

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var schedule = await _context.MailingSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException("Schedule", id.ToString());

            // Campaigns produced by this schedule keep their history — the FK is SetNull.
            _context.MailingSchedules.Remove(schedule);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task EnsureTemplateExistsAsync(int templateId, CancellationToken cancellationToken)
        {
            var exists = await _context.MailingTemplates.AnyAsync(t => t.Id == templateId, cancellationToken);
            if (!exists) throw new NotFoundException("Template", templateId.ToString());
        }

        private static void Validate(UpsertScheduleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ValidationException("A schedule needs a name.");

            if (request.Cadence == ScheduleCadence.FixedInterval && request.IntervalMinutes < 15)
            {
                // Below this the scheduler's own poll interval makes the cadence meaningless,
                // and it stops looking like human-paced outreach.
                throw new ValidationException("The interval must be at least 15 minutes.");
            }

            if (request.BatchSize is < 1 or > 500)
                throw new ValidationException("Batch size must be between 1 and 500.");

            if (request.DelayBetweenEmailsMs is < 0 or > 60000)
                throw new ValidationException("The delay between emails must be between 0 and 60000 ms.");

            if (request.SendWindowStartHourUtc is < 0 or > 23 || request.SendWindowEndHourUtc is < 0 or > 23)
                throw new ValidationException("Send window hours must be between 0 and 23.");

            if (request.MaxTotalSends is < 1)
                throw new ValidationException("The send cap must be at least 1, or empty for no cap.");
        }

        private static void Apply(MailingSchedule schedule, UpsertScheduleRequest request)
        {
            schedule.Name = request.Name.Trim();
            schedule.TemplateId = request.TemplateId;
            schedule.Audience = request.Audience.Audience;
            schedule.FirmTypeIds = Join(request.Audience.FirmTypeIds);
            schedule.FirmGroupIds = Join(request.Audience.FirmGroupIds);
            schedule.SelectedFirmIds = Join(request.Audience.FirmIds);
            schedule.Cadence = request.Cadence;
            schedule.IntervalMinutes = request.IntervalMinutes;
            schedule.IsEnabled = request.IsEnabled;
            schedule.BatchSize = request.BatchSize;
            schedule.DelayBetweenEmailsMs = request.DelayBetweenEmailsMs;
            schedule.SendWindowStartHourUtc = request.SendWindowStartHourUtc;
            schedule.SendWindowEndHourUtc = request.SendWindowEndHourUtc;
            schedule.SkipAlreadyContacted = request.SkipAlreadyContacted;
            schedule.MaxTotalSends = request.MaxTotalSends;
            schedule.ProviderKey = request.ProviderKey;
            schedule.CustomFieldsJson = request.CustomFields.Count > 0
                ? JsonSerializer.Serialize(request.CustomFields)
                : null;
        }

        private static AudienceSelection ToSelection(MailingSchedule s) => new()
        {
            Audience = s.Audience,
            FirmTypeIds = Split(s.FirmTypeIds),
            FirmGroupIds = Split(s.FirmGroupIds),
            FirmIds = Split(s.SelectedFirmIds)
        };

        private static MailingScheduleDto Map(MailingSchedule s) => new()
        {
            Id = s.Id,
            Name = s.Name,
            TemplateId = s.TemplateId,
            TemplateName = s.Template?.Name ?? string.Empty,
            Audience = s.Audience,
            Cadence = s.Cadence,
            IntervalMinutes = s.IntervalMinutes,
            NextRunAt = s.NextRunAt,
            LastRunAt = s.LastRunAt,
            IsEnabled = s.IsEnabled,
            BatchSize = s.BatchSize,
            DelayBetweenEmailsMs = s.DelayBetweenEmailsMs,
            SendWindowStartHourUtc = s.SendWindowStartHourUtc,
            SendWindowEndHourUtc = s.SendWindowEndHourUtc,
            SkipAlreadyContacted = s.SkipAlreadyContacted,
            MaxTotalSends = s.MaxTotalSends,
            TotalSent = s.TotalSent,
            ProviderKey = s.ProviderKey,
            LastError = s.LastError,
            CreatedByName = s.CreatedByName
        };

        private static string? Join(List<int> values) =>
            values.Count == 0 ? null : string.Join(",", values);

        private static List<int> Split(string? csv) =>
            string.IsNullOrWhiteSpace(csv)
                ? new List<int>()
                : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .Select(v => int.TryParse(v, out var i) ? i : (int?)null)
                     .Where(i => i.HasValue)
                     .Select(i => i!.Value)
                     .ToList();
    }
}
