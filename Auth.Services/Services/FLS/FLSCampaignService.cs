using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs.Email;
using Auth.Models.DTOs.FLS;
using Auth.Models.Entities;
using Auth.Models.Entities.FLS;
using Auth.Models.Enums.FLS;
using Auth.Models.Request.FLS;
using Auth.Services.Interfaces.Email;
using Auth.Services.Interfaces.FLS;
using Auth.Services.Services.Email;
using Auth.Services.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Services.Services.FLS
{
    public class FLSCampaignService : IFLSCampaignService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IEmailDispatcher _dispatcher;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly EmailOptions _emailOptions;
        private readonly ILogger<FLSCampaignService> _logger;

        /// <summary>Uploads a speaker must provide before their profile counts as complete.</summary>
        private static readonly UploadType[] RequiredUploads =
        {
            UploadType.CV, UploadType.Picture, UploadType.Synopsis, UploadType.Presentation
        };

        /// <summary>Roles treated as "FLS people" for the <c>FlsStaff</c> audience.</summary>
        private static readonly string[] StaffRoles =
        {
            AppRoles.Admin, AppRoles.FLSAdmin, AppRoles.PartnerMember
        };

        public FLSCampaignService(
            ApplicationDbContext context,
            UserManager<User> userManager,
            IEmailDispatcher dispatcher,
            IEmailTemplateRenderer renderer,
            IOptions<EmailOptions> emailOptions,
            ILogger<FLSCampaignService> logger)
        {
            _context = context;
            _userManager = userManager;
            _dispatcher = dispatcher;
            _renderer = renderer;
            _emailOptions = emailOptions.Value;
            _logger = logger;
        }

        // ── Directory ────────────────────────────────────────────────────────────

        public async Task<List<DirectoryRecipientDto>> GetRecipientDirectoryAsync(
            CancellationToken cancellationToken = default)
        {
            var speakers = await LoadSpeakersAsync(includeDeregistered: true, cancellationToken);

            var directory = speakers
                .Where(s => s.Profile.User is not null)
                .Select(s => new DirectoryRecipientDto
                {
                    Email = s.Profile.User!.Email ?? string.Empty,
                    Name = FullName(s.Profile.User),
                    UserId = s.Profile.UserId,
                    SpeakerProfileId = s.Profile.Id,
                    Kind = "Speaker",
                    Organization = s.Profile.Organization,
                    SpeakerType = s.Profile.SpeakerType.ToString(),
                    HasIncompleteUploads = s.HasIncompleteUploads,
                    IsDeregistered = s.Profile.IsDeregistered
                })
                .ToList();

            foreach (var (user, role) in await LoadStaffAsync(cancellationToken))
            {
                // A staff member who is also a speaker is already listed above; listing
                // them twice would mail them twice on a combined send.
                if (directory.Any(d => string.Equals(d.UserId, user.Id, StringComparison.Ordinal)))
                    continue;

                directory.Add(new DirectoryRecipientDto
                {
                    Email = user.Email ?? string.Empty,
                    Name = FullName(user),
                    UserId = user.Id,
                    Kind = role
                });
            }

            return directory
                .Where(d => !string.IsNullOrWhiteSpace(d.Email))
                .OrderBy(d => d.Kind)
                .ThenBy(d => d.Name)
                .ToList();
        }

        public EmailSettingsDto GetEmailSettings()
        {
            var defaultKey = _dispatcher.DefaultProviderKey;

            return new EmailSettingsDto
            {
                Providers = _dispatcher.GetProviders().Select(p => new EmailProviderDto
                {
                    Key = p.Key,
                    DisplayName = p.DisplayName,
                    IsConfigured = p.IsConfigured,
                    ConfigurationHint = p.ConfigurationHint,
                    IsDefault = string.Equals(p.Key, defaultKey, StringComparison.OrdinalIgnoreCase)
                }).ToList(),
                DefaultProvider = defaultKey,
                FallbackEnabled = _emailOptions.EnableFallback,
                FallbackOrder = _emailOptions.FallbackOrder,
                SandboxMode = _emailOptions.IsSandboxed,
                SandboxRedirectTo = _emailOptions.SandboxRedirectTo,
                SendDelayMs = _emailOptions.SendDelayMs,
                MaxRecipientsPerCampaign = _emailOptions.MaxRecipientsPerCampaign,
                Variables = TemplateVariables.Supported
                    .Select(v => new TemplateVariableDto { Name = v.Name, Description = v.Description })
                    .ToList()
            };
        }

        // ── Preview ──────────────────────────────────────────────────────────────

        public async Task<CampaignPreviewDto> PreviewAsync(
            PreviewCampaignRequest request,
            CancellationToken cancellationToken = default)
        {
            Validate(request);

            var targets = await ResolveAudienceAsync(request, cancellationToken);
            var warnings = new List<string>();

            var deliverable = targets.Where(t => !string.IsNullOrWhiteSpace(t.Email)).ToList();
            var undeliverable = targets.Count - deliverable.Count;
            if (undeliverable > 0)
                warnings.Add($"{undeliverable} recipient(s) will be skipped — no email address on file.");

            if (deliverable.Count > _emailOptions.MaxRecipientsPerCampaign)
            {
                warnings.Add(
                    $"This audience has {deliverable.Count} recipients but the per-campaign limit is " +
                    $"{_emailOptions.MaxRecipientsPerCampaign}. Narrow the audience or raise " +
                    "EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN.");
            }

            // Render against the first real recipient so the preview shows actual data,
            // falling back to placeholder text when the audience is empty.
            var sample = deliverable.FirstOrDefault();
            var variables = sample?.Variables ?? SampleVariables(request.Deadline);

            var rendered = _renderer.Render(request.Subject, request.Body, variables);

            return new CampaignPreviewDto
            {
                RecipientCount = deliverable.Count,
                SampleRecipients = deliverable.Take(5).Select(t => $"{t.Name} <{t.Email}>").ToList(),
                RenderedSubject = rendered.Subject,
                RenderedHtml = rendered.HtmlBody,
                RenderedText = rendered.TextBody,
                UnresolvedVariables = rendered.UnresolvedVariables.ToList(),
                Warnings = warnings,
                ProviderKey = request.ProviderKey ?? _dispatcher.DefaultProviderKey,
                SandboxMode = _emailOptions.IsSandboxed
            };
        }

        public async Task<bool> SendTestEmailAsync(
            SendCampaignRequest request,
            string toEmail,
            CancellationToken cancellationToken = default)
        {
            Validate(request);

            if (string.IsNullOrWhiteSpace(toEmail))
                throw new ArgumentException("A test recipient address is required.", nameof(toEmail));

            // Use real audience data when available so the test exercises the same
            // substitution the real send will perform.
            var targets = await ResolveAudienceAsync(request, cancellationToken);
            var variables = targets.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Email))?.Variables
                            ?? SampleVariables(request.Deadline);

            var rendered = _renderer.Render($"[TEST] {request.Subject}", request.Body, variables);

            var result = await _dispatcher.SendAsync(new OutboundEmail
            {
                ToEmail = toEmail,
                Subject = rendered.Subject,
                HtmlBody = rendered.HtmlBody,
                TextBody = rendered.TextBody,
                Tag = "fls-campaign-test"
            }, request.ProviderKey, cancellationToken);

            if (!result.Success)
                _logger.LogWarning("Test email to {Email} failed via {Provider}: {Error}",
                    toEmail, result.Provider, result.Error);

            return result.Success;
        }

        // ── Send ─────────────────────────────────────────────────────────────────

        public async Task<EmailCampaignDetailDto> SendAsync(
            SendCampaignRequest request,
            string userId,
            string userName,
            CancellationToken cancellationToken = default)
        {
            Validate(request);

            var targets = (await ResolveAudienceAsync(request, cancellationToken))
                .Where(t => !string.IsNullOrWhiteSpace(t.Email))
                .ToList();

            if (targets.Count == 0)
                throw new InvalidOperationException("This audience matched no recipients with an email address.");

            if (targets.Count > _emailOptions.MaxRecipientsPerCampaign)
            {
                throw new InvalidOperationException(
                    $"Refusing to send to {targets.Count} recipients — the limit is " +
                    $"{_emailOptions.MaxRecipientsPerCampaign} (EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN).");
            }

            var campaign = new EmailCampaign
            {
                Subject = request.Subject,
                Body = request.Body,
                Audience = request.Audience,
                SpeakerTypeFilter = request.SpeakerTypeFilter,
                SelectedSpeakerIds = request.SpeakerProfileIds is { Count: > 0 }
                    ? string.Join(",", request.SpeakerProfileIds)
                    : null,
                ProviderKey = request.ProviderKey,
                Deadline = request.Deadline,
                Status = CampaignStatus.Sending,
                TotalRecipients = targets.Count,
                CreatedByUserId = userId,
                CreatedByName = userName,
                CreatedAt = DateTime.UtcNow
            };

            foreach (var target in targets)
            {
                campaign.Recipients.Add(new EmailCampaignRecipient
                {
                    Email = target.Email,
                    RecipientName = target.Name,
                    UserId = target.UserId,
                    SpeakerProfileId = target.SpeakerProfileId,
                    Status = EmailDeliveryStatus.Pending
                });
            }

            _context.EmailCampaigns.Add(campaign);
            await _context.SaveChangesAsync(cancellationToken);

            // The campaign row is persisted BEFORE any email goes out. If the process dies
            // mid-send the history still shows the campaign as Sending with per-recipient
            // state, rather than losing the record of a partially delivered blast.
            var recipients = campaign.Recipients.ToList();

            for (var i = 0; i < targets.Count; i++)
            {
                await DeliverAsync(recipients[i], targets[i], request, campaign, cancellationToken);

                if (_emailOptions.SendDelayMs > 0 && i < targets.Count - 1)
                    await Task.Delay(_emailOptions.SendDelayMs, cancellationToken);
            }

            if (request.AlsoCreateInAppNotification)
                AddInAppNotifications(campaign, targets, request);

            FinaliseStatus(campaign);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Campaign {Id} finished: {Sent}/{Total} delivered, {Failed} failed.",
                campaign.Id, campaign.SentCount, campaign.TotalRecipients, campaign.FailedCount);

            return MapDetail(campaign);
        }

        public async Task<EmailCampaignDetailDto> RetryFailedAsync(
            int campaignId,
            string? providerKey,
            CancellationToken cancellationToken = default)
        {
            var campaign = await _context.EmailCampaigns
                .Include(c => c.Recipients)
                .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken)
                ?? throw new InvalidOperationException($"Campaign {campaignId} not found.");

            var failed = campaign.Recipients.Where(r => r.Status == EmailDeliveryStatus.Failed).ToList();
            if (failed.Count == 0) return MapDetail(campaign);

            campaign.Status = CampaignStatus.Sending;

            // Retries re-render from the campaign's stored template. Speaker-specific
            // variables aren't reloaded here — a retry targets a known address whose
            // personalisation was already resolved at first send.
            var request = new SendCampaignRequest
            {
                Subject = campaign.Subject,
                Body = campaign.Body,
                Audience = campaign.Audience,
                Deadline = campaign.Deadline,
                ProviderKey = providerKey ?? campaign.ProviderKey
            };

            foreach (var recipient in failed)
            {
                var target = new ResolvedTarget(
                    recipient.Email,
                    recipient.RecipientName ?? string.Empty,
                    recipient.UserId,
                    recipient.SpeakerProfileId,
                    BuildVariablesForRetry(recipient, campaign.Deadline));

                recipient.Error = null;
                await DeliverAsync(recipient, target, request, campaign, cancellationToken);

                if (_emailOptions.SendDelayMs > 0)
                    await Task.Delay(_emailOptions.SendDelayMs, cancellationToken);
            }

            FinaliseStatus(campaign);
            await _context.SaveChangesAsync(cancellationToken);

            return MapDetail(campaign);
        }

        private async Task DeliverAsync(
            EmailCampaignRecipient recipient,
            ResolvedTarget target,
            SendCampaignRequest request,
            EmailCampaign campaign,
            CancellationToken cancellationToken)
        {
            var rendered = _renderer.Render(request.Subject, request.Body, target.Variables);

            var result = await _dispatcher.SendAsync(new OutboundEmail
            {
                ToEmail = target.Email,
                ToName = target.Name,
                Subject = rendered.Subject,
                HtmlBody = rendered.HtmlBody,
                TextBody = rendered.TextBody,
                Tag = $"fls-campaign-{campaign.Id}"
            }, request.ProviderKey, cancellationToken);

            recipient.ProviderUsed = result.Provider;
            recipient.ProviderMessageId = result.MessageId;

            if (result.Success)
            {
                recipient.Status = EmailDeliveryStatus.Sent;
                recipient.SentAt = DateTime.UtcNow;
            }
            else
            {
                recipient.Status = EmailDeliveryStatus.Failed;
                recipient.Error = Truncate(result.Error ?? "Unknown error", 500);
            }
        }

        /// <summary>
        /// Mirrors the campaign into the in-app notification feed for speaker recipients,
        /// so the message survives a spam filter.
        /// </summary>
        private void AddInAppNotifications(
            EmailCampaign campaign,
            IReadOnlyList<ResolvedTarget> targets,
            SendCampaignRequest request)
        {
            var speakerIds = targets
                .Where(t => t.SpeakerProfileId.HasValue)
                .Select(t => t.SpeakerProfileId!.Value)
                .Distinct()
                .ToList();

            if (speakerIds.Count == 0) return;

            _context.SpeakerNotifications.AddRange(speakerIds.Select(id => new SpeakerNotification
            {
                SpeakerProfileId = id,
                NotificationType = FLSNotificationType.General,
                Title = campaign.Subject,
                Message = request.Body,
                EmailSent = true,
                CreatedAt = DateTime.UtcNow
            }));
        }

        private static void FinaliseStatus(EmailCampaign campaign)
        {
            campaign.SentCount = campaign.Recipients.Count(r => r.Status == EmailDeliveryStatus.Sent);
            campaign.FailedCount = campaign.Recipients.Count(r => r.Status == EmailDeliveryStatus.Failed);
            campaign.CompletedAt = DateTime.UtcNow;

            campaign.Status = campaign.FailedCount switch
            {
                0 => CampaignStatus.Completed,
                _ when campaign.SentCount == 0 => CampaignStatus.Failed,
                _ => CampaignStatus.PartiallyFailed
            };
        }

        // ── History ──────────────────────────────────────────────────────────────

        public async Task<List<EmailCampaignSummaryDto>> GetCampaignsAsync(
            int take = 50,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 200);

            var campaigns = await _context.EmailCampaigns
                .AsNoTracking()
                .OrderByDescending(c => c.CreatedAt)
                .Take(take)
                .ToListAsync(cancellationToken);

            return campaigns.Select(MapSummary).ToList();
        }

        public async Task<EmailCampaignDetailDto?> GetCampaignAsync(
            int campaignId,
            CancellationToken cancellationToken = default)
        {
            var campaign = await _context.EmailCampaigns
                .AsNoTracking()
                .Include(c => c.Recipients)
                .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);

            return campaign is null ? null : MapDetail(campaign);
        }

        // ── Audience resolution ──────────────────────────────────────────────────

        /// <summary>A recipient plus the template variables that personalise their copy.</summary>
        private sealed record ResolvedTarget(
            string Email,
            string Name,
            string? UserId,
            int? SpeakerProfileId,
            Dictionary<string, string?> Variables);

        private async Task<List<ResolvedTarget>> ResolveAudienceAsync(
            SendCampaignRequest request,
            CancellationToken cancellationToken)
        {
            return request.Audience switch
            {
                CampaignAudience.ActiveSpeakers =>
                    await SpeakerTargetsAsync(request, cancellationToken, s => !s.Profile.IsDeregistered),

                CampaignAudience.SpeakersWithIncompleteUploads =>
                    await SpeakerTargetsAsync(request, cancellationToken,
                        s => !s.Profile.IsDeregistered && s.HasIncompleteUploads),

                CampaignAudience.SpeakersByType =>
                    await SpeakerTargetsAsync(request, cancellationToken,
                        s => !s.Profile.IsDeregistered && s.Profile.SpeakerType == request.SpeakerTypeFilter),

                CampaignAudience.DeregisteredSpeakers =>
                    await SpeakerTargetsAsync(request, cancellationToken, s => s.Profile.IsDeregistered),

                CampaignAudience.SelectedSpeakers =>
                    await SpeakerTargetsAsync(request, cancellationToken,
                        s => request.SpeakerProfileIds!.Contains(s.Profile.Id)),

                CampaignAudience.FlsStaff =>
                    await StaffTargetsAsync(request, cancellationToken),

                _ => throw new ArgumentOutOfRangeException(
                    nameof(request), $"Unsupported audience '{request.Audience}'.")
            };
        }

        private async Task<List<ResolvedTarget>> SpeakerTargetsAsync(
            SendCampaignRequest request,
            CancellationToken cancellationToken,
            Func<SpeakerWithCompletion, bool> predicate)
        {
            var speakers = await LoadSpeakersAsync(includeDeregistered: true, cancellationToken);

            return speakers
                .Where(predicate)
                .Where(s => s.Profile.User is not null)
                .Select(s => new ResolvedTarget(
                    s.Profile.User!.Email ?? string.Empty,
                    FullName(s.Profile.User),
                    s.Profile.UserId,
                    s.Profile.Id,
                    TemplateVariables.ForSpeaker(s.Profile, s.Profile.User, request.Deadline)))
                .ToList();
        }

        private async Task<List<ResolvedTarget>> StaffTargetsAsync(
            SendCampaignRequest request,
            CancellationToken cancellationToken)
        {
            var staff = await LoadStaffAsync(cancellationToken);

            return staff
                .Select(s => new ResolvedTarget(
                    s.User.Email ?? string.Empty,
                    FullName(s.User),
                    s.User.Id,
                    null,
                    TemplateVariables.ForUser(s.User, request.Deadline)))
                .ToList();
        }

        private sealed record SpeakerWithCompletion(SpeakerProfile Profile, bool HasIncompleteUploads);

        private async Task<List<SpeakerWithCompletion>> LoadSpeakersAsync(
            bool includeDeregistered,
            CancellationToken cancellationToken)
        {
            var query = _context.SpeakerProfiles
                .AsNoTracking()
                .Include(sp => sp.User)
                .Include(sp => sp.Uploads)
                .AsQueryable();

            if (!includeDeregistered)
                query = query.Where(sp => !sp.IsDeregistered);

            var profiles = await query.ToListAsync(cancellationToken);

            return profiles
                .Select(p => new SpeakerWithCompletion(
                    p,
                    RequiredUploads.Any(type => p.Uploads.All(u => u.UploadType != type))))
                .ToList();
        }

        private sealed record StaffMember(User User, string Role);

        /// <summary>
        /// Loads FLS staff accounts. Uses one query per role rather than
        /// <c>GetUsersInRoleAsync</c> per user, and de-duplicates people holding more than
        /// one staff role so a combined send never mails anyone twice.
        /// </summary>
        private async Task<List<StaffMember>> LoadStaffAsync(CancellationToken cancellationToken)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var staff = new List<StaffMember>();

            foreach (var role in StaffRoles)
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role);

                foreach (var user in usersInRole)
                {
                    if (string.IsNullOrWhiteSpace(user.Email)) continue;
                    if (!user.IsActive) continue;
                    if (!seen.Add(user.Id)) continue;

                    staff.Add(new StaffMember(user, role));
                }
            }

            return staff.OrderBy(s => s.User.Email).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void Validate(SendCampaignRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.Subject))
                throw new ArgumentException("Subject is required.", nameof(request));

            if (string.IsNullOrWhiteSpace(request.Body))
                throw new ArgumentException("Message body is required.", nameof(request));

            if (request.Audience == CampaignAudience.SpeakersByType && request.SpeakerTypeFilter is null)
                throw new ArgumentException("A speaker type is required for the 'by type' audience.", nameof(request));

            if (request.Audience == CampaignAudience.SelectedSpeakers &&
                (request.SpeakerProfileIds is null || request.SpeakerProfileIds.Count == 0))
            {
                throw new ArgumentException("Select at least one speaker.", nameof(request));
            }
        }

        private static Dictionary<string, string?> SampleVariables(string? deadline) => new(StringComparer.OrdinalIgnoreCase)
        {
            ["firstName"] = "Amina",
            ["lastName"] = "Hodžić",
            ["fullName"] = "Amina Hodžić",
            ["email"] = "speaker@example.org",
            ["organization"] = "Example Organisation",
            ["speakerType"] = "Plenary",
            ["year"] = DateTime.UtcNow.Year.ToString(),
            ["deadline"] = deadline ?? string.Empty,
            ["portalUrl"] = TemplateVariables.PortalUrl
        };

        private static Dictionary<string, string?> BuildVariablesForRetry(
            EmailCampaignRecipient recipient,
            string? deadline)
        {
            var name = recipient.RecipientName ?? string.Empty;
            var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["firstName"] = parts.Length > 0 ? parts[0] : string.Empty,
                ["lastName"] = parts.Length > 1 ? parts[1] : string.Empty,
                ["fullName"] = name,
                ["email"] = recipient.Email,
                ["organization"] = string.Empty,
                ["speakerType"] = string.Empty,
                ["year"] = DateTime.UtcNow.Year.ToString(),
                ["deadline"] = deadline ?? string.Empty,
                ["portalUrl"] = TemplateVariables.PortalUrl
            };
        }

        private static string FullName(User user) =>
            $"{user.FirstName} {user.LastName}".Trim();

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max];

        private static EmailCampaignSummaryDto MapSummary(EmailCampaign c) => new()
        {
            Id = c.Id,
            Subject = c.Subject,
            Audience = c.Audience,
            AudienceLabel = AudienceLabel(c.Audience),
            Status = c.Status,
            StatusLabel = c.Status.ToString(),
            ProviderKey = c.ProviderKey,
            TotalRecipients = c.TotalRecipients,
            SentCount = c.SentCount,
            FailedCount = c.FailedCount,
            CreatedByName = c.CreatedByName,
            CreatedAt = c.CreatedAt,
            CompletedAt = c.CompletedAt
        };

        private static EmailCampaignDetailDto MapDetail(EmailCampaign c) => new()
        {
            Id = c.Id,
            Subject = c.Subject,
            Body = c.Body,
            Audience = c.Audience,
            AudienceLabel = AudienceLabel(c.Audience),
            Status = c.Status,
            StatusLabel = c.Status.ToString(),
            ProviderKey = c.ProviderKey,
            Deadline = c.Deadline,
            TotalRecipients = c.TotalRecipients,
            SentCount = c.SentCount,
            FailedCount = c.FailedCount,
            CreatedByName = c.CreatedByName,
            CreatedAt = c.CreatedAt,
            CompletedAt = c.CompletedAt,
            Recipients = c.Recipients
                .OrderBy(r => r.Status == EmailDeliveryStatus.Failed ? 0 : 1)
                .ThenBy(r => r.Email)
                .Select(r => new CampaignRecipientDto
                {
                    Id = r.Id,
                    Email = r.Email,
                    RecipientName = r.RecipientName,
                    SpeakerProfileId = r.SpeakerProfileId,
                    Status = r.Status,
                    StatusLabel = r.Status.ToString(),
                    ProviderUsed = r.ProviderUsed,
                    Error = r.Error,
                    SentAt = r.SentAt
                }).ToList()
        };

        private static string AudienceLabel(CampaignAudience audience) => audience switch
        {
            CampaignAudience.ActiveSpeakers => "All active speakers",
            CampaignAudience.SpeakersWithIncompleteUploads => "Speakers with missing uploads",
            CampaignAudience.SpeakersByType => "Speakers by type",
            CampaignAudience.DeregisteredSpeakers => "Deregistered speakers",
            CampaignAudience.FlsStaff => "FLS staff",
            CampaignAudience.SelectedSpeakers => "Selected speakers",
            _ => audience.ToString()
        };
    }
}
