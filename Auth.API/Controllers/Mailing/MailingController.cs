using Auth.Models.Constants;
using Auth.Models.DTOs.Mailing;
using Auth.Models.Request.Mailing;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Email;
using Auth.Services.Interfaces.Mailing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers.Mailing
{
    /// <summary>
    /// Taxonomy, templates, campaigns, schedules and provider status for the partnerships
    /// mailing module.
    /// </summary>
    [Route("api/mailing")]
    [ApiController]
    [Authorize(Roles = AppRoles.Mailing)]
    public class MailingController : ControllerBase
    {
        private readonly IMailingTaxonomyService _taxonomy;
        private readonly IMailingCampaignService _campaigns;
        private readonly IMailingScheduleService _schedules;
        private readonly IEmailDispatcher _dispatcher;
        private readonly IAuditService _audit;
        private readonly ILogger<MailingController> _logger;

        public MailingController(
            IMailingTaxonomyService taxonomy,
            IMailingCampaignService campaigns,
            IMailingScheduleService schedules,
            IEmailDispatcher dispatcher,
            IAuditService audit,
            ILogger<MailingController> logger)
        {
            _taxonomy = taxonomy;
            _campaigns = campaigns;
            _schedules = schedules;
            _dispatcher = dispatcher;
            _audit = audit;
            _logger = logger;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        private string UserName
        {
            get
            {
                var name = $"{User.FindFirstValue("FirstName")} {User.FindFirstValue("LastName")}".Trim();
                return name.Length > 0 ? name : User.FindFirstValue(ClaimTypes.Email) ?? "Unknown";
            }
        }

        // ── Taxonomy ──────────────────────────────────────────────────────────

        [HttpGet("groups")]
        public async Task<ActionResult<ApiResponse<List<FirmGroupDto>>>> GetGroups(CancellationToken ct) =>
            Ok(ApiResponse<List<FirmGroupDto>>.SuccessResponse(await _taxonomy.GetGroupsAsync(ct), "Groups retrieved"));

        [HttpPost("groups")]
        public async Task<ActionResult<ApiResponse<FirmGroupDto>>> CreateGroup(
            [FromBody] UpsertFirmGroupRequest request, CancellationToken ct) =>
            Ok(ApiResponse<FirmGroupDto>.SuccessResponse(await _taxonomy.CreateGroupAsync(request, ct), "Group created"));

        [HttpPut("groups/{id:int}")]
        public async Task<ActionResult<ApiResponse<FirmGroupDto>>> UpdateGroup(
            int id, [FromBody] UpsertFirmGroupRequest request, CancellationToken ct) =>
            Ok(ApiResponse<FirmGroupDto>.SuccessResponse(await _taxonomy.UpdateGroupAsync(id, request, ct), "Group updated"));

        [HttpDelete("groups/{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteGroup(int id, CancellationToken ct)
        {
            await _taxonomy.DeleteGroupAsync(id, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Group deleted"));
        }

        [HttpGet("types")]
        public async Task<ActionResult<ApiResponse<List<FirmTypeDto>>>> GetTypes(CancellationToken ct) =>
            Ok(ApiResponse<List<FirmTypeDto>>.SuccessResponse(await _taxonomy.GetTypesAsync(ct), "Types retrieved"));

        [HttpPost("types")]
        public async Task<ActionResult<ApiResponse<FirmTypeDto>>> CreateType(
            [FromBody] UpsertFirmTypeRequest request, CancellationToken ct) =>
            Ok(ApiResponse<FirmTypeDto>.SuccessResponse(await _taxonomy.CreateTypeAsync(request, ct), "Type created"));

        [HttpPut("types/{id:int}")]
        public async Task<ActionResult<ApiResponse<FirmTypeDto>>> UpdateType(
            int id, [FromBody] UpsertFirmTypeRequest request, CancellationToken ct) =>
            Ok(ApiResponse<FirmTypeDto>.SuccessResponse(await _taxonomy.UpdateTypeAsync(id, request, ct), "Type updated"));

        [HttpDelete("types/{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteType(int id, CancellationToken ct)
        {
            await _taxonomy.DeleteTypeAsync(id, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Type deleted"));
        }

        // ── Templates ─────────────────────────────────────────────────────────

        [HttpGet("templates")]
        public async Task<ActionResult<ApiResponse<List<MailingTemplateDto>>>> GetTemplates(
            [FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
            Ok(ApiResponse<List<MailingTemplateDto>>.SuccessResponse(
                await _campaigns.GetTemplatesAsync(includeInactive, ct), "Templates retrieved"));

        [HttpGet("templates/{id:int}")]
        public async Task<ActionResult<ApiResponse<MailingTemplateDto>>> GetTemplate(int id, CancellationToken ct)
        {
            var template = await _campaigns.GetTemplateAsync(id, ct);

            return template is null
                ? NotFound(ApiResponse<MailingTemplateDto>.ErrorResponse("Template not found."))
                : Ok(ApiResponse<MailingTemplateDto>.SuccessResponse(template, "Template retrieved"));
        }

        [HttpPost("templates")]
        public async Task<ActionResult<ApiResponse<MailingTemplateDto>>> CreateTemplate(
            [FromBody] UpsertTemplateRequest request, CancellationToken ct) =>
            Ok(ApiResponse<MailingTemplateDto>.SuccessResponse(
                await _campaigns.CreateTemplateAsync(request, UserId, ct), "Template created"));

        [HttpPut("templates/{id:int}")]
        public async Task<ActionResult<ApiResponse<MailingTemplateDto>>> UpdateTemplate(
            int id, [FromBody] UpsertTemplateRequest request, CancellationToken ct) =>
            Ok(ApiResponse<MailingTemplateDto>.SuccessResponse(
                await _campaigns.UpdateTemplateAsync(id, request, ct), "Template updated"));

        [HttpDelete("templates/{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteTemplate(int id, CancellationToken ct)
        {
            await _campaigns.DeleteTemplateAsync(id, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Template deleted"));
        }

        // ── Campaigns ─────────────────────────────────────────────────────────

        /// <summary>Renders the campaign without sending, so nothing goes out unreviewed.</summary>
        [HttpPost("campaigns/preview")]
        public async Task<ActionResult<ApiResponse<CampaignPreviewDto>>> Preview(
            [FromBody] PreviewCampaignRequest request, CancellationToken ct) =>
            Ok(ApiResponse<CampaignPreviewDto>.SuccessResponse(
                await _campaigns.PreviewAsync(request, ct), "Preview generated"));

        [HttpPost("campaigns/send")]
        public async Task<ActionResult<ApiResponse<MailingCampaignDto>>> Send(
            [FromBody] SendMailingCampaignRequest request, CancellationToken ct)
        {
            var campaign = await _campaigns.SendAsync(request, UserId, UserName, ct);

            await _audit.LogAsync(
                string.IsNullOrWhiteSpace(request.TestRecipientEmail) ? "Mailing.CampaignSent" : "Mailing.TestSent",
                userId: UserId,
                payload: $"Template={request.TemplateId} Audience={request.Audience.Audience} " +
                         $"Sent={campaign.SentCount} Failed={campaign.FailedCount} Skipped={campaign.SkippedCount}",
                ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(ApiResponse<MailingCampaignDto>.SuccessResponse(
                campaign,
                $"{campaign.SentCount} sent, {campaign.FailedCount} failed, {campaign.SkippedCount} skipped"));
        }

        [HttpGet("campaigns")]
        public async Task<ActionResult<ApiResponse<List<MailingCampaignDto>>>> GetCampaigns(
            [FromQuery] int limit = 50, CancellationToken ct = default) =>
            Ok(ApiResponse<List<MailingCampaignDto>>.SuccessResponse(
                await _campaigns.GetCampaignsAsync(limit, ct), "Campaigns retrieved"));

        [HttpGet("campaigns/{id:int}")]
        public async Task<ActionResult<ApiResponse<MailingCampaignDto>>> GetCampaign(int id, CancellationToken ct)
        {
            var campaign = await _campaigns.GetCampaignAsync(id, ct);

            return campaign is null
                ? NotFound(ApiResponse<MailingCampaignDto>.ErrorResponse("Campaign not found."))
                : Ok(ApiResponse<MailingCampaignDto>.SuccessResponse(campaign, "Campaign retrieved"));
        }

        [HttpGet("campaigns/{id:int}/recipients")]
        public async Task<ActionResult<ApiResponse<List<MailingCampaignRecipientDto>>>> GetRecipients(
            int id, CancellationToken ct) =>
            Ok(ApiResponse<List<MailingCampaignRecipientDto>>.SuccessResponse(
                await _campaigns.GetRecipientsAsync(id, ct), "Recipients retrieved"));

        [HttpPost("campaigns/{id:int}/retry")]
        public async Task<ActionResult<ApiResponse<MailingCampaignDto>>> Retry(int id, CancellationToken ct)
        {
            var campaign = await _campaigns.RetryFailedAsync(id, ct);

            await _audit.LogAsync("Mailing.CampaignRetried", userId: UserId, payload: $"Campaign={id}");

            return Ok(ApiResponse<MailingCampaignDto>.SuccessResponse(campaign, "Failed recipients retried"));
        }

        // ── Schedules ─────────────────────────────────────────────────────────

        [HttpGet("schedules")]
        public async Task<ActionResult<ApiResponse<List<MailingScheduleDto>>>> GetSchedules(CancellationToken ct) =>
            Ok(ApiResponse<List<MailingScheduleDto>>.SuccessResponse(
                await _schedules.GetAllAsync(ct), "Schedules retrieved"));

        [HttpPost("schedules")]
        public async Task<ActionResult<ApiResponse<MailingScheduleDto>>> CreateSchedule(
            [FromBody] UpsertScheduleRequest request, CancellationToken ct)
        {
            var schedule = await _schedules.CreateAsync(request, UserId, UserName, ct);
            await _audit.LogAsync("Mailing.ScheduleCreated", userId: UserId, payload: schedule.Name);
            return Ok(ApiResponse<MailingScheduleDto>.SuccessResponse(schedule, "Schedule created"));
        }

        [HttpPut("schedules/{id:int}")]
        public async Task<ActionResult<ApiResponse<MailingScheduleDto>>> UpdateSchedule(
            int id, [FromBody] UpsertScheduleRequest request, CancellationToken ct) =>
            Ok(ApiResponse<MailingScheduleDto>.SuccessResponse(
                await _schedules.UpdateAsync(id, request, ct), "Schedule updated"));

        [HttpPost("schedules/{id:int}/enabled")]
        public async Task<ActionResult<ApiResponse<bool>>> SetScheduleEnabled(
            int id, [FromQuery] bool enabled, CancellationToken ct)
        {
            await _schedules.SetEnabledAsync(id, enabled, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, enabled ? "Schedule resumed" : "Schedule paused"));
        }

        [HttpDelete("schedules/{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteSchedule(int id, CancellationToken ct)
        {
            await _schedules.DeleteAsync(id, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Schedule deleted"));
        }

        // ── Providers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Which email providers are configured. Reports the provider key, display name and
        /// what's missing — never the credentials themselves.
        /// </summary>
        [HttpGet("providers")]
        public ActionResult<ApiResponse<object>> GetProviders()
        {
            var providers = _dispatcher.GetProviders()
                .Select(p => new
                {
                    key = p.Key,
                    name = p.DisplayName,
                    isConfigured = p.IsConfigured,
                    hint = p.ConfigurationHint,
                    isDefault = p.Key == _dispatcher.DefaultProviderKey
                })
                .ToList();

            return Ok(ApiResponse<object>.SuccessResponse(
                new { providers, defaultProvider = _dispatcher.DefaultProviderKey },
                "Providers retrieved"));
        }
    }
}
