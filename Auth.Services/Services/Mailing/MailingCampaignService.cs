using Auth.Models.Data;
using Auth.Models.DTOs.Email;
using Auth.Models.DTOs.Mailing;
using Auth.Models.Entities.Mailing;
using Auth.Models.Enums.FLS;
using Auth.Models.Enums.Mailing;
using Auth.Models.Exceptions;
using Auth.Models.Request.Mailing;
using Auth.Services.Interfaces.Email;
using Auth.Services.Interfaces.Mailing;
using Auth.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Auth.Services.Services.Mailing
{
    public class MailingCampaignService : IMailingCampaignService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailDispatcher _dispatcher;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly IEmailSuppressionService _suppression;
        private readonly EmailOptions _emailOptions;
        private readonly ILogger<MailingCampaignService> _logger;

        public MailingCampaignService(
            ApplicationDbContext context,
            IEmailDispatcher dispatcher,
            IEmailTemplateRenderer renderer,
            IEmailSuppressionService suppression,
            IOptions<EmailOptions> emailOptions,
            ILogger<MailingCampaignService> logger)
        {
            _context = context;
            _dispatcher = dispatcher;
            _renderer = renderer;
            _suppression = suppression;
            _emailOptions = emailOptions.Value;
            _logger = logger;
        }

        // ── Templates ─────────────────────────────────────────────────────────

        public async Task<List<MailingTemplateDto>> GetTemplatesAsync(
            bool includeInactive = false, CancellationToken cancellationToken = default)
        {
            var query = _context.MailingTemplates
                .AsNoTracking()
                .Include(t => t.FirmType)
                .AsQueryable();

            if (!includeInactive) query = query.Where(t => t.IsActive);

            var templates = await query.OrderBy(t => t.Name).ToListAsync(cancellationToken);
            return templates.Select(MapTemplate).ToList();
        }

        public async Task<MailingTemplateDto?> GetTemplateAsync(int id, CancellationToken cancellationToken = default)
        {
            var template = await _context.MailingTemplates
                .AsNoTracking()
                .Include(t => t.FirmType)
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

            return template is null ? null : MapTemplate(template);
        }

        public async Task<MailingTemplateDto> CreateTemplateAsync(
            UpsertTemplateRequest request, string? userId, CancellationToken cancellationToken = default)
        {
            Validate(request);

            var template = new MailingTemplate { CreatedByUserId = userId };
            ApplyTemplate(template, request);

            _context.MailingTemplates.Add(template);
            await _context.SaveChangesAsync(cancellationToken);

            return (await GetTemplateAsync(template.Id, cancellationToken))!;
        }

        public async Task<MailingTemplateDto> UpdateTemplateAsync(
            int id, UpsertTemplateRequest request, CancellationToken cancellationToken = default)
        {
            Validate(request);

            var template = await _context.MailingTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new NotFoundException("Template", id.ToString());

            ApplyTemplate(template, request);
            template.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return (await GetTemplateAsync(id, cancellationToken))!;
        }

        public async Task DeleteTemplateAsync(int id, CancellationToken cancellationToken = default)
        {
            var template = await _context.MailingTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new NotFoundException("Template", id.ToString());

            // Schedules reference templates with Restrict, so deleting one out from under a
            // live automation would fail at the database. Say so usefully instead.
            var scheduleCount = await _context.MailingSchedules.CountAsync(s => s.TemplateId == id, cancellationToken);

            if (scheduleCount > 0)
            {
                throw new ValidationException(
                    $"This template is used by {scheduleCount} schedule(s). Delete or repoint them first.");
            }

            _context.MailingTemplates.Remove(template);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Audience ──────────────────────────────────────────────────────────

        public async Task<List<Firm>> ResolveAudienceAsync(
            AudienceSelection selection, CancellationToken cancellationToken = default)
        {
            var query = _context.Firms
                .AsNoTracking()
                .Include(f => f.FirmType)
                .AsQueryable();

            // Every audience starts from contactable firms. The dispatcher enforces this too,
            // but filtering here means we don't build hundreds of recipients just to drop them.
            query = query.Where(f => f.Status == FirmStatus.Active && f.NormalizedEmail != null);

            query = selection.Audience switch
            {
                FirmAudience.ByFirmType =>
                    query.Where(f => f.FirmTypeId != null && selection.FirmTypeIds.Contains(f.FirmTypeId.Value)),

                FirmAudience.ByFirmGroup =>
                    query.Where(f => f.FirmType != null
                                     && f.FirmType.FirmGroupId != null
                                     && selection.FirmGroupIds.Contains(f.FirmType.FirmGroupId.Value)),

                FirmAudience.SelectedFirms =>
                    query.Where(f => selection.FirmIds.Contains(f.Id)),

                FirmAudience.WithContactName =>
                    query.Where(f => f.ContactPersonName != null && f.ContactNameConfidence >= NameConfidence.Medium),

                FirmAudience.WithoutContactName =>
                    query.Where(f => f.ContactPersonName == null || f.ContactNameConfidence < NameConfidence.Medium),

                FirmAudience.NeverContacted =>
                    query.Where(f => f.LastContactedAt == null),

                _ => query
            };

            return await query
                .OrderBy(f => f.Name)
                .Take(_emailOptions.MaxRecipientsPerCampaign)
                .ToListAsync(cancellationToken);
        }

        public async Task<CampaignPreviewDto> PreviewAsync(
            PreviewCampaignRequest request, CancellationToken cancellationToken = default)
        {
            var template = await _context.MailingTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken)
                ?? throw new NotFoundException("Template", request.TemplateId.ToString());

            var firms = await ResolveAudienceAsync(request.Audience, cancellationToken);

            var suppressed = await _suppression.CheckManyAsync(
                firms.Where(f => f.NormalizedEmail != null).Select(f => f.NormalizedEmail!),
                cancellationToken);

            var preview = new CampaignPreviewDto { TotalMatched = firms.Count };
            var unresolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sampleLimit = Math.Clamp(request.SampleSize, 1, 25);

            foreach (var firm in firms)
            {
                var variant = template.ResolveVariant(firm.HasUsableContactName);

                if (variant == TemplateVariant.Person) preview.PersonVariantCount++;
                else preview.FirmVariantCount++;

                var isSuppressed = firm.NormalizedEmail is not null
                    && suppressed.TryGetValue(firm.NormalizedEmail, out var check);

                if (isSuppressed) preview.SuppressedCount++;
                else preview.Sendable++;

                var rendered = _renderer.Render(
                    template.ResolveSubject(variant),
                    template.ResolveBody(variant),
                    BuildVariables(firm, variant, request.CustomFields));

                foreach (var name in rendered.UnresolvedVariables) unresolved.Add(name);

                if (preview.Samples.Count < sampleLimit)
                {
                    preview.Samples.Add(new CampaignPreviewItemDto
                    {
                        FirmId = firm.Id,
                        FirmName = firm.Name,
                        ToEmail = firm.Email,
                        ToName = variant == TemplateVariant.Person ? firm.ContactPersonName : firm.Name,
                        VariantUsed = variant,
                        Subject = rendered.Subject,
                        HtmlBody = rendered.HtmlBody,
                        UnresolvedVariables = rendered.UnresolvedVariables.ToList(),
                        IsSuppressed = isSuppressed,
                        SuppressionReason = isSuppressed && firm.NormalizedEmail is not null
                            ? suppressed[firm.NormalizedEmail].Explanation
                            : null
                    });
                }
            }

            preview.UnresolvedVariables = unresolved.ToList();
            return preview;
        }

        // ── Sending ───────────────────────────────────────────────────────────

        public async Task<MailingCampaignDto> SendAsync(
            SendMailingCampaignRequest request, string userId, string userName,
            CancellationToken cancellationToken = default)
        {
            var template = await _context.MailingTemplates
                .FirstOrDefaultAsync(t => t.Id == request.TemplateId, cancellationToken)
                ?? throw new NotFoundException("Template", request.TemplateId.ToString());

            // A test send renders against the first matching firm so the operator sees the
            // real thing — placeholders expanded with real data — rather than a mock.
            if (!string.IsNullOrWhiteSpace(request.TestRecipientEmail))
                return await SendTestAsync(request, template, userId, userName, cancellationToken);

            var firms = await ResolveAudienceAsync(request.Audience, cancellationToken);

            if (firms.Count == 0)
                throw new ValidationException("No contactable firms match this audience.");

            var campaign = new MailingCampaign
            {
                Name = string.IsNullOrWhiteSpace(request.Name) ? template.Name : request.Name.Trim(),
                TemplateId = template.Id,
                SubjectFirmVariant = template.SubjectFirmVariant,
                BodyFirmVariant = template.BodyFirmVariant,
                PersonVariantEnabled = template.PersonVariantEnabled,
                SubjectPersonVariant = template.SubjectPersonVariant,
                BodyPersonVariant = template.BodyPersonVariant,
                Audience = request.Audience.Audience,
                FirmTypeIds = Join(request.Audience.FirmTypeIds),
                FirmGroupIds = Join(request.Audience.FirmGroupIds),
                SelectedFirmIds = Join(request.Audience.FirmIds),
                ProviderKey = request.ProviderKey,
                CustomFieldsJson = request.CustomFields.Count > 0
                    ? JsonSerializer.Serialize(request.CustomFields)
                    : null,
                Status = MailingCampaignStatus.Sending,
                TotalRecipients = firms.Count,
                CreatedByUserId = userId,
                CreatedByName = userName,
                StartedAt = DateTime.UtcNow
            };

            _context.MailingCampaigns.Add(campaign);
            await _context.SaveChangesAsync(cancellationToken);

            await DeliverAsync(campaign, template, firms, request.CustomFields, cancellationToken);

            return (await GetCampaignAsync(campaign.Id, cancellationToken))!;
        }

        /// <summary>
        /// Sends each recipient in turn, recording the outcome per firm.
        ///
        /// Sequential with a configurable pause rather than parallel: bulk outreach that
        /// arrives as a burst from one IP is the classic spam signature, and the free tiers
        /// this is likely to run on rate-limit aggressively.
        /// </summary>
        private async Task DeliverAsync(
            MailingCampaign campaign,
            MailingTemplate template,
            List<Firm> firms,
            Dictionary<string, string> customFields,
            CancellationToken cancellationToken)
        {
            var delay = Math.Max(0, _emailOptions.SendDelayMs);
            var trackedFirms = await _context.Firms
                .Where(f => firms.Select(x => x.Id).Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, cancellationToken);

            foreach (var firm in firms)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var variant = template.ResolveVariant(firm.HasUsableContactName);
                var toName = variant == TemplateVariant.Person ? firm.ContactPersonName : firm.Name;

                var rendered = _renderer.Render(
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

                var result = await _dispatcher.SendAsync(
                    new OutboundEmail
                    {
                        ToEmail = firm.Email ?? string.Empty,
                        ToName = toName,
                        Subject = rendered.Subject,
                        HtmlBody = rendered.HtmlBody,
                        TextBody = rendered.TextBody,
                        Tag = $"mailing-campaign-{campaign.Id}"
                    },
                    campaign.ProviderKey,
                    cancellationToken);

                ApplyResult(recipient, result);

                if (result.Success)
                {
                    campaign.SentCount++;

                    // Drives the "never contacted" audience and the frequency display.
                    if (trackedFirms.TryGetValue(firm.Id, out var tracked))
                    {
                        tracked.LastContactedAt = DateTime.UtcNow;
                        tracked.ContactCount++;
                    }
                }
                else if (result.WasSuppressed)
                {
                    campaign.SkippedCount++;
                }
                else
                {
                    campaign.FailedCount++;
                }

                _context.MailingCampaignRecipients.Add(recipient);

                if (delay > 0) await Task.Delay(delay, cancellationToken);
            }

            campaign.Status = campaign.SentCount == 0 && campaign.FailedCount > 0
                ? MailingCampaignStatus.Failed
                : campaign.FailedCount > 0
                    ? MailingCampaignStatus.PartiallyFailed
                    : MailingCampaignStatus.Completed;

            campaign.CompletedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Campaign {Id} finished: {Sent} sent, {Failed} failed, {Skipped} skipped.",
                campaign.Id, campaign.SentCount, campaign.FailedCount, campaign.SkippedCount);
        }

        private async Task<MailingCampaignDto> SendTestAsync(
            SendMailingCampaignRequest request,
            MailingTemplate template,
            string userId,
            string userName,
            CancellationToken cancellationToken)
        {
            var firms = await ResolveAudienceAsync(request.Audience, cancellationToken);
            var sample = firms.FirstOrDefault();

            // With no matching firm there is still something useful to show: the template
            // rendered against placeholder data.
            var variant = sample is not null
                ? template.ResolveVariant(sample.HasUsableContactName)
                : TemplateVariant.Firm;

            var variables = sample is not null
                ? BuildVariables(sample, variant, request.CustomFields)
                : SampleVariables(request.CustomFields);

            var rendered = _renderer.Render(
                template.ResolveSubject(variant), template.ResolveBody(variant), variables);

            var result = await _dispatcher.SendAsync(
                new OutboundEmail
                {
                    ToEmail = request.TestRecipientEmail!,
                    Subject = $"[TEST] {rendered.Subject}",
                    HtmlBody = rendered.HtmlBody,
                    TextBody = rendered.TextBody,
                    Tag = "mailing-test"
                },
                request.ProviderKey,
                cancellationToken);

            if (!result.Success)
                throw new ValidationException($"Test send failed: {result.Error}");

            _logger.LogInformation("Test send of template {Id} to {Email}.", template.Id, request.TestRecipientEmail);

            return new MailingCampaignDto
            {
                Name = $"Test — {template.Name}",
                TemplateId = template.Id,
                TemplateName = template.Name,
                Status = MailingCampaignStatus.Completed,
                TotalRecipients = 1,
                SentCount = 1,
                CreatedByName = userName,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
        }

        // ── History ───────────────────────────────────────────────────────────

        public async Task<List<MailingCampaignDto>> GetCampaignsAsync(
            int limit = 50, CancellationToken cancellationToken = default)
        {
            var campaigns = await _context.MailingCampaigns
                .AsNoTracking()
                .Include(c => c.Template)
                .OrderByDescending(c => c.CreatedAt)
                .Take(Math.Clamp(limit, 1, 200))
                .ToListAsync(cancellationToken);

            return campaigns.Select(MapCampaign).ToList();
        }

        public async Task<MailingCampaignDto?> GetCampaignAsync(int id, CancellationToken cancellationToken = default)
        {
            var campaign = await _context.MailingCampaigns
                .AsNoTracking()
                .Include(c => c.Template)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

            return campaign is null ? null : MapCampaign(campaign);
        }

        public async Task<List<MailingCampaignRecipientDto>> GetRecipientsAsync(
            int campaignId, CancellationToken cancellationToken = default)
        {
            return await _context.MailingCampaignRecipients
                .AsNoTracking()
                .Include(r => r.Firm)
                .Where(r => r.CampaignId == campaignId)
                .OrderBy(r => r.Firm.Name)
                .Select(r => new MailingCampaignRecipientDto
                {
                    Id = r.Id,
                    FirmId = r.FirmId,
                    FirmName = r.Firm.Name,
                    ToEmail = r.ToEmail,
                    ToName = r.ToName,
                    VariantUsed = r.VariantUsed,
                    RenderedSubject = r.RenderedSubject,
                    Status = r.Status.ToString(),
                    ProviderUsed = r.ProviderUsed,
                    Error = r.Error,
                    AttemptCount = r.AttemptCount,
                    SentAt = r.SentAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<MailingCampaignDto> RetryFailedAsync(
            int campaignId, CancellationToken cancellationToken = default)
        {
            var campaign = await _context.MailingCampaigns
                .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken)
                ?? throw new NotFoundException("Campaign", campaignId.ToString());

            // Only Failed. Skipped means suppressed — retrying would mail someone who is
            // deactivated or has unsubscribed.
            var failed = await _context.MailingCampaignRecipients
                .Include(r => r.Firm)
                .Where(r => r.CampaignId == campaignId && r.Status == EmailDeliveryStatus.Failed)
                .ToListAsync(cancellationToken);

            if (failed.Count == 0)
                throw new ValidationException("This campaign has no failed recipients to retry.");

            var customFields = string.IsNullOrWhiteSpace(campaign.CustomFieldsJson)
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(campaign.CustomFieldsJson)
                    ?? new Dictionary<string, string>();

            var delay = Math.Max(0, _emailOptions.SendDelayMs);

            foreach (var recipient in failed)
            {
                var variant = recipient.VariantUsed;

                // Re-rendered from the campaign's snapshot, not the live template — a retry
                // must send what the campaign said, even if the template changed since.
                var subject = variant == TemplateVariant.Person && campaign.SubjectPersonVariant is not null
                    ? campaign.SubjectPersonVariant
                    : campaign.SubjectFirmVariant;

                var body = variant == TemplateVariant.Person && campaign.BodyPersonVariant is not null
                    ? campaign.BodyPersonVariant
                    : campaign.BodyFirmVariant;

                var rendered = _renderer.Render(
                    subject, body, BuildVariables(recipient.Firm, variant, customFields));

                var result = await _dispatcher.SendAsync(
                    new OutboundEmail
                    {
                        ToEmail = recipient.ToEmail,
                        ToName = recipient.ToName,
                        Subject = rendered.Subject,
                        HtmlBody = rendered.HtmlBody,
                        TextBody = rendered.TextBody,
                        Tag = $"mailing-campaign-{campaign.Id}-retry"
                    },
                    campaign.ProviderKey,
                    cancellationToken);

                recipient.AttemptCount++;
                ApplyResult(recipient, result);

                if (result.Success)
                {
                    campaign.SentCount++;
                    campaign.FailedCount = Math.Max(0, campaign.FailedCount - 1);

                    recipient.Firm.LastContactedAt = DateTime.UtcNow;
                    recipient.Firm.ContactCount++;
                }
                else if (result.WasSuppressed)
                {
                    campaign.SkippedCount++;
                    campaign.FailedCount = Math.Max(0, campaign.FailedCount - 1);
                }

                if (delay > 0) await Task.Delay(delay, cancellationToken);
            }

            campaign.Status = campaign.FailedCount > 0
                ? MailingCampaignStatus.PartiallyFailed
                : MailingCampaignStatus.Completed;

            await _context.SaveChangesAsync(cancellationToken);

            return (await GetCampaignAsync(campaignId, cancellationToken))!;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void ApplyResult(MailingCampaignRecipient recipient, EmailSendResult result)
        {
            if (result.Success)
            {
                recipient.Status = EmailDeliveryStatus.Sent;
                recipient.ProviderUsed = result.Provider;
                recipient.ProviderMessageId = result.MessageId;
                recipient.SentAt = DateTime.UtcNow;
                recipient.Error = null;
                return;
            }

            recipient.Status = result.WasSuppressed ? EmailDeliveryStatus.Skipped : EmailDeliveryStatus.Failed;
            recipient.Error = result.Error;
            recipient.ProviderUsed = result.Provider;
        }

        /// <summary>
        /// Placeholder values for one firm. The person variant addresses the contact by
        /// given name; the firm variant addresses the organisation.
        /// </summary>
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

                // Resolves to the person's name on the person variant and the organisation's
                // on the firm variant, so one greeting line works in both templates.
                ["greetingName"] = variant == TemplateVariant.Person && !string.IsNullOrWhiteSpace(firstName)
                    ? firstName
                    : firm.Name
            };

            // Operator-supplied fields last so a campaign can override a computed default.
            foreach (var (key, value) in customFields)
                variables[key] = value;

            return variables;
        }

        private static Dictionary<string, string?> SampleVariables(Dictionary<string, string> customFields)
        {
            var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["firmName"] = "Example d.o.o.",
                ["legalName"] = "Example d.o.o. Sarajevo",
                ["firmType"] = "IT Company",
                ["email"] = "example@example.ba",
                ["city"] = "Sarajevo",
                ["country"] = "Bosnia and Herzegovina",
                ["website"] = "https://example.ba",
                ["contactName"] = "Amir Hodzic",
                ["contactRole"] = "Direktor",
                ["firstName"] = "Amir",
                ["greetingName"] = "Amir",
                ["year"] = DateTime.UtcNow.Year.ToString()
            };

            foreach (var (key, value) in customFields) variables[key] = value;
            return variables;
        }

        private void Validate(UpsertTemplateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ValidationException("A template needs a name.");

            if (string.IsNullOrWhiteSpace(request.SubjectFirmVariant) ||
                string.IsNullOrWhiteSpace(request.BodyFirmVariant))
            {
                // The firm variant is the fallback for every firm without a known contact,
                // so it is the one that cannot be optional.
                throw new ValidationException("The firm-addressed subject and body are required.");
            }

            if (request.PersonVariantEnabled &&
                (string.IsNullOrWhiteSpace(request.SubjectPersonVariant) ||
                 string.IsNullOrWhiteSpace(request.BodyPersonVariant)))
            {
                throw new ValidationException(
                    "The person-addressed variant is enabled but its subject or body is empty.");
            }
        }

        private static void ApplyTemplate(MailingTemplate template, UpsertTemplateRequest request)
        {
            template.Name = request.Name.Trim();
            template.Description = request.Description;
            template.FirmTypeId = request.FirmTypeId;
            template.SubjectFirmVariant = request.SubjectFirmVariant.Trim();
            template.BodyFirmVariant = request.BodyFirmVariant;
            template.PersonVariantEnabled = request.PersonVariantEnabled;
            template.SubjectPersonVariant = request.SubjectPersonVariant?.Trim();
            template.BodyPersonVariant = request.BodyPersonVariant;
            template.IsActive = request.IsActive;
        }

        private MailingTemplateDto MapTemplate(MailingTemplate t)
        {
            var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in _renderer.ExtractVariableNames(t.SubjectFirmVariant)) variables.Add(name);
            foreach (var name in _renderer.ExtractVariableNames(t.BodyFirmVariant)) variables.Add(name);
            foreach (var name in _renderer.ExtractVariableNames(t.SubjectPersonVariant ?? "")) variables.Add(name);
            foreach (var name in _renderer.ExtractVariableNames(t.BodyPersonVariant ?? "")) variables.Add(name);

            return new MailingTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                Description = t.Description,
                FirmTypeId = t.FirmTypeId,
                FirmTypeName = t.FirmType?.Name,
                SubjectFirmVariant = t.SubjectFirmVariant,
                BodyFirmVariant = t.BodyFirmVariant,
                PersonVariantEnabled = t.PersonVariantEnabled,
                SubjectPersonVariant = t.SubjectPersonVariant,
                BodyPersonVariant = t.BodyPersonVariant,
                IsActive = t.IsActive,
                SupportsPersonVariant = t.SupportsPersonVariant,
                Variables = variables.OrderBy(v => v).ToList(),
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt
            };
        }

        private static MailingCampaignDto MapCampaign(MailingCampaign c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            TemplateId = c.TemplateId,
            TemplateName = c.Template?.Name,
            Audience = c.Audience,
            Status = c.Status,
            ProviderKey = c.ProviderKey,
            TotalRecipients = c.TotalRecipients,
            SentCount = c.SentCount,
            FailedCount = c.FailedCount,
            SkippedCount = c.SkippedCount,
            CreatedByName = c.CreatedByName,
            CreatedAt = c.CreatedAt,
            CompletedAt = c.CompletedAt,
            WasScheduled = c.ScheduleId.HasValue
        };

        private static string? Join(List<int> values) =>
            values.Count == 0 ? null : string.Join(",", values);
    }
}
