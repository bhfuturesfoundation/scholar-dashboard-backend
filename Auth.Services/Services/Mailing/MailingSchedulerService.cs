using Auth.Models.Data;
using Auth.Models.DTOs.Email;
using Auth.Models.Entities.Mailing;
using Auth.Models.Enums.FLS;
using Auth.Models.Enums.Mailing;
using Auth.Services.Interfaces.Email;
using Auth.Services.Interfaces.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Auth.Services.Services.Mailing
{
    /// <summary>
    /// Runs due mailing schedules in the background.
    ///
    /// Each run materialises a real <see cref="MailingCampaign"/>, so an automated send
    /// leaves exactly the same audit trail as a manual one — the history screen doesn't need
    /// to know the difference, and a scheduled send can be inspected and retried identically.
    ///
    /// The batching, pacing and send-window controls are deliberate deliverability measures,
    /// not conveniences. Mailing 400 firms in one burst at 03:00 is the fastest way into a
    /// spam folder; small batches during business hours, spaced apart, look like a person.
    /// </summary>
    public class MailingSchedulerService : BackgroundService
    {
        /// <summary>
        /// How often to look for due schedules. A minute is far more often than any schedule
        /// fires, but keeps "next run" accurate without meaningful cost — the query is a
        /// single indexed lookup.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MailingSchedulerService> _logger;

        public MailingSchedulerService(IServiceScopeFactory scopeFactory, ILogger<MailingSchedulerService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Mailing scheduler started; polling every {Interval}.", PollInterval);

            // Let the app finish starting before touching the database.
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunDueSchedulesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A BackgroundService that throws is torn down permanently and silently.
                    // Swallowing here keeps the scheduler alive across transient failures.
                    _logger.LogError(ex, "Scheduler tick failed. Continuing.");
                }

                try { await Task.Delay(PollInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("Mailing scheduler stopped.");
        }

        private async Task RunDueSchedulesAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;

            var due = await context.MailingSchedules
                .Include(s => s.Template)
                .Where(s => s.IsEnabled && s.NextRunAt != null && s.NextRunAt <= now)
                .ToListAsync(cancellationToken);

            if (due.Count == 0) return;

            foreach (var schedule in due)
            {
                if (cancellationToken.IsCancellationRequested) break;

                // Outside the send window: leave NextRunAt alone so it fires as soon as the
                // window opens, rather than being pushed a whole cadence into the future.
                if (!schedule.IsWithinSendWindow(now))
                {
                    _logger.LogDebug(
                        "Schedule {Id} is due but outside its {Start}:00-{End}:00 UTC window; waiting.",
                        schedule.Id, schedule.SendWindowStartHourUtc, schedule.SendWindowEndHourUtc);
                    continue;
                }

                if (schedule.HasReachedCap)
                {
                    schedule.IsEnabled = false;
                    schedule.NextRunAt = null;
                    schedule.LastError = $"Reached the configured cap of {schedule.MaxTotalSends} sends.";
                    await context.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation("Schedule {Id} disabled — send cap reached.", schedule.Id);
                    continue;
                }

                try
                {
                    await RunOneAsync(scope.ServiceProvider, context, schedule, cancellationToken);
                }
                catch (Exception ex)
                {
                    // One broken schedule must not stop the others.
                    _logger.LogError(ex, "Schedule {Id} failed.", schedule.Id);

                    schedule.LastError = ex.Message;
                    schedule.LastRunAt = DateTime.UtcNow;
                    schedule.NextRunAt = schedule.ComputeNextRun(DateTime.UtcNow);

                    await context.SaveChangesAsync(cancellationToken);
                }
            }
        }

        private async Task RunOneAsync(
            IServiceProvider services,
            ApplicationDbContext context,
            MailingSchedule schedule,
            CancellationToken cancellationToken)
        {
            var campaignService = services.GetRequiredService<IMailingCampaignService>();
            var dispatcher = services.GetRequiredService<IEmailDispatcher>();
            var renderer = services.GetRequiredService<IEmailTemplateRenderer>();

            var selection = new Models.Request.Mailing.AudienceSelection
            {
                Audience = schedule.Audience,
                FirmTypeIds = Split(schedule.FirmTypeIds),
                FirmGroupIds = Split(schedule.FirmGroupIds),
                FirmIds = Split(schedule.SelectedFirmIds)
            };

            var firms = await campaignService.ResolveAudienceAsync(selection, cancellationToken);

            if (schedule.SkipAlreadyContacted)
            {
                // Firms this schedule has already delivered to. This is what turns a recurring
                // schedule into a drip that works through the list, rather than one that mails
                // the same firms over and over.
                var alreadyContacted = await context.MailingCampaignRecipients
                    .Where(r => r.Campaign.ScheduleId == schedule.Id && r.Status == EmailDeliveryStatus.Sent)
                    .Select(r => r.FirmId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var contacted = alreadyContacted.ToHashSet();
                firms = firms.Where(f => !contacted.Contains(f.Id)).ToList();
            }

            var remainingCap = schedule.MaxTotalSends.HasValue
                ? Math.Max(0, schedule.MaxTotalSends.Value - schedule.TotalSent)
                : int.MaxValue;

            var batch = firms.Take(Math.Min(Math.Max(1, schedule.BatchSize), remainingCap)).ToList();

            if (batch.Count == 0)
            {
                // The list is exhausted. Disable rather than waking up forever to do nothing.
                schedule.LastRunAt = DateTime.UtcNow;
                schedule.NextRunAt = schedule.ComputeNextRun(DateTime.UtcNow);
                schedule.LastError = null;

                if (schedule.SkipAlreadyContacted)
                {
                    schedule.IsEnabled = false;
                    schedule.NextRunAt = null;
                    schedule.LastError = "Every firm in this audience has been contacted.";
                    _logger.LogInformation("Schedule {Id} disabled — audience exhausted.", schedule.Id);
                }

                await context.SaveChangesAsync(cancellationToken);
                return;
            }

            var template = schedule.Template;

            var customFields = string.IsNullOrWhiteSpace(schedule.CustomFieldsJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(schedule.CustomFieldsJson)
                    ?? new Dictionary<string, string>();

            var campaign = new MailingCampaign
            {
                Name = $"{schedule.Name} — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
                TemplateId = template.Id,
                ScheduleId = schedule.Id,
                SubjectFirmVariant = template.SubjectFirmVariant,
                BodyFirmVariant = template.BodyFirmVariant,
                PersonVariantEnabled = template.PersonVariantEnabled,
                SubjectPersonVariant = template.SubjectPersonVariant,
                BodyPersonVariant = template.BodyPersonVariant,
                Audience = schedule.Audience,
                FirmTypeIds = schedule.FirmTypeIds,
                FirmGroupIds = schedule.FirmGroupIds,
                SelectedFirmIds = schedule.SelectedFirmIds,
                ProviderKey = schedule.ProviderKey,
                CustomFieldsJson = schedule.CustomFieldsJson,
                Status = MailingCampaignStatus.Sending,
                TotalRecipients = batch.Count,
                CreatedByUserId = schedule.CreatedByUserId,
                CreatedByName = $"{schedule.CreatedByName} (scheduled)",
                StartedAt = DateTime.UtcNow
            };

            context.MailingCampaigns.Add(campaign);
            await context.SaveChangesAsync(cancellationToken);

            var trackedFirms = await context.Firms
                .Where(f => batch.Select(b => b.Id).Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, cancellationToken);

            foreach (var firm in batch)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var variant = template.ResolveVariant(firm.HasUsableContactName);
                var toName = variant == TemplateVariant.Person ? firm.ContactPersonName : firm.Name;

                var rendered = renderer.Render(
                    template.ResolveSubject(variant),
                    template.ResolveBody(variant),
                    BuildVariables(firm, variant, customFields));

                var recipient = new MailingCampaignRecipient
                {
                    CampaignId = campaign.Id,
                    FirmId = firm.Id,
                    ToEmail = firm.Email ?? string.Empty,
                    ToName = toName,
                    VariantUsed = variant,
                    RenderedSubject = rendered.Subject,
                    AttemptCount = 1
                };

                var result = await dispatcher.SendAsync(
                    new OutboundEmail
                    {
                        ToEmail = firm.Email ?? string.Empty,
                        ToName = toName,
                        Subject = rendered.Subject,
                        HtmlBody = rendered.HtmlBody,
                        TextBody = rendered.TextBody,
                        Tag = $"mailing-schedule-{schedule.Id}"
                    },
                    schedule.ProviderKey,
                    cancellationToken);

                if (result.Success)
                {
                    recipient.Status = EmailDeliveryStatus.Sent;
                    recipient.ProviderUsed = result.Provider;
                    recipient.ProviderMessageId = result.MessageId;
                    recipient.SentAt = DateTime.UtcNow;

                    campaign.SentCount++;
                    schedule.TotalSent++;

                    if (trackedFirms.TryGetValue(firm.Id, out var tracked))
                    {
                        tracked.LastContactedAt = DateTime.UtcNow;
                        tracked.ContactCount++;
                    }
                }
                else if (result.WasSuppressed)
                {
                    recipient.Status = EmailDeliveryStatus.Skipped;
                    recipient.Error = result.Error;
                    campaign.SkippedCount++;
                }
                else
                {
                    recipient.Status = EmailDeliveryStatus.Failed;
                    recipient.Error = result.Error;
                    recipient.ProviderUsed = result.Provider;
                    campaign.FailedCount++;
                }

                context.MailingCampaignRecipients.Add(recipient);

                // Paced per-email, not per-batch: a burst of 25 from one IP is the pattern
                // spam filters look for, even if the batches themselves are hours apart.
                if (schedule.DelayBetweenEmailsMs > 0)
                    await Task.Delay(schedule.DelayBetweenEmailsMs, cancellationToken);
            }

            campaign.Status = campaign.SentCount == 0 && campaign.FailedCount > 0
                ? MailingCampaignStatus.Failed
                : campaign.FailedCount > 0
                    ? MailingCampaignStatus.PartiallyFailed
                    : MailingCampaignStatus.Completed;

            campaign.CompletedAt = DateTime.UtcNow;

            schedule.LastRunAt = DateTime.UtcNow;
            schedule.LastCampaignId = campaign.Id;
            schedule.LastError = null;
            schedule.NextRunAt = schedule.ComputeNextRun(DateTime.UtcNow);

            // A one-shot schedule has no next run; disable rather than leaving it armed.
            if (schedule.NextRunAt is null) schedule.IsEnabled = false;

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Schedule {Id} ran: {Sent} sent, {Failed} failed, {Skipped} skipped. Next run {Next}.",
                schedule.Id, campaign.SentCount, campaign.FailedCount, campaign.SkippedCount,
                schedule.NextRunAt?.ToString("u") ?? "none");
        }

        private static Dictionary<string, string?> BuildVariables(
            Firm firm, TemplateVariant variant, Dictionary<string, string> customFields)
        {
            var contactName = firm.ContactPersonName ?? string.Empty;
            var firstName = contactName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

            var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["firmName"] = firm.Name,
                ["legalName"] = firm.LegalName ?? firm.Name,
                ["firmType"] = firm.FirmType?.Name ?? string.Empty,
                ["email"] = firm.Email ?? string.Empty,
                ["city"] = firm.City ?? string.Empty,
                ["country"] = firm.Country ?? string.Empty,
                ["website"] = firm.Website ?? string.Empty,
                ["contactName"] = contactName,
                ["contactRole"] = firm.ContactPersonRole ?? string.Empty,
                ["firstName"] = firstName,
                ["year"] = DateTime.UtcNow.Year.ToString(),
                ["greetingName"] = variant == TemplateVariant.Person && !string.IsNullOrWhiteSpace(firstName)
                    ? firstName
                    : firm.Name
            };

            foreach (var (key, value) in customFields) variables[key] = value;
            return variables;
        }

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
