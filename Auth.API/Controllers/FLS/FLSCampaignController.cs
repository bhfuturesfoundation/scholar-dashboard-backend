using Auth.Models.Constants;
using Auth.Models.Request.FLS;
using Auth.Models.Response;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers.FLS
{
    /// <summary>
    /// Outbound FLS communications. This is the partner-member workspace: compose,
    /// preview, send and review email to speakers and FLS staff.
    ///
    /// Authorised to <see cref="AppRoles.FlsCommunications"/> — partner members plus
    /// FLS admins and platform admins. Partner members deliberately have no access to
    /// upload verification, meetings, documents or speaker records.
    /// </summary>
    [Route("api/fls/campaigns")]
    [ApiController]
    [Authorize(Roles = AppRoles.FlsCommunications)]
    public class FLSCampaignController : ControllerBase
    {
        private readonly IFLSCampaignService _campaignService;
        private readonly ILogger<FLSCampaignController> _logger;

        public FLSCampaignController(IFLSCampaignService campaignService, ILogger<FLSCampaignController> logger)
        {
            _campaignService = campaignService;
            _logger = logger;
        }

        private string GetUserId() =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? string.Empty;

        private string GetUserName()
        {
            var first = User.FindFirstValue("FirstName") ?? string.Empty;
            var last = User.FindFirstValue("LastName") ?? string.Empty;
            var name = $"{first} {last}".Trim();

            return string.IsNullOrWhiteSpace(name)
                ? User.FindFirstValue(ClaimTypes.Email) ?? "Unknown"
                : name;
        }

        /// <summary>Providers, defaults, sandbox state and the supported template variables.</summary>
        [HttpGet("settings")]
        public IActionResult GetSettings()
        {
            var settings = _campaignService.GetEmailSettings();
            return Ok(ApiResponse<object>.SuccessResponse(settings, ""));
        }

        /// <summary>Everyone who can be mailed — speakers and FLS staff.</summary>
        [HttpGet("recipients")]
        public async Task<IActionResult> GetRecipients(CancellationToken cancellationToken)
        {
            var recipients = await _campaignService.GetRecipientDirectoryAsync(cancellationToken);
            return Ok(ApiResponse<object>.SuccessResponse(recipients, $"{recipients.Count} recipient(s)."));
        }

        /// <summary>
        /// Dry run. Resolves the audience and renders the message without sending anything —
        /// this is what surfaces unresolved <c>{{placeholders}}</c> before a broadcast goes out.
        /// </summary>
        [HttpPost("preview")]
        public async Task<IActionResult> Preview(
            [FromBody] PreviewCampaignRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var preview = await _campaignService.PreviewAsync(request, cancellationToken);
                return Ok(ApiResponse<object>.SuccessResponse(preview, ""));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        /// <summary>Sends the composed message to one address so the sender can check it themselves.</summary>
        [HttpPost("test")]
        public async Task<IActionResult> SendTest(
            [FromBody] SendTestEmailRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var sent = await _campaignService.SendTestEmailAsync(
                    request.Campaign, request.ToEmail, cancellationToken);

                return sent
                    ? Ok(ApiResponse<bool>.SuccessResponse(true, $"Test email sent to {request.ToEmail}."))
                    : BadRequest(ApiResponse<bool>.ErrorResponse(
                        "The test email could not be sent. Check the provider configuration in Settings."));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse(ex.Message));
            }
        }

        [HttpPost("send")]
        public async Task<IActionResult> Send(
            [FromBody] SendCampaignRequest request,
            CancellationToken cancellationToken)
        {
            try
            {
                var campaign = await _campaignService.SendAsync(
                    request, GetUserId(), GetUserName(), cancellationToken);

                var message = campaign.FailedCount == 0
                    ? $"Sent to {campaign.SentCount} recipient(s)."
                    : $"Sent to {campaign.SentCount} of {campaign.TotalRecipients}; {campaign.FailedCount} failed.";

                return Ok(ApiResponse<object>.SuccessResponse(campaign, message));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                // Audience empty or over the configured recipient cap — a user-fixable
                // problem, so 400 rather than letting the middleware turn it into a 500.
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetCampaigns([FromQuery] int take = 50, CancellationToken cancellationToken = default)
        {
            var campaigns = await _campaignService.GetCampaignsAsync(take, cancellationToken);
            return Ok(ApiResponse<object>.SuccessResponse(campaigns, ""));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetCampaign(int id, CancellationToken cancellationToken)
        {
            var campaign = await _campaignService.GetCampaignAsync(id, cancellationToken);
            return campaign is null
                ? NotFound(ApiResponse<object>.ErrorResponse("Campaign not found."))
                : Ok(ApiResponse<object>.SuccessResponse(campaign, ""));
        }

        /// <summary>Re-sends only the recipients that failed, optionally through a different provider.</summary>
        [HttpPost("{id:int}/retry-failed")]
        public async Task<IActionResult> RetryFailed(
            int id,
            [FromBody] RetryCampaignRequest? request,
            CancellationToken cancellationToken)
        {
            try
            {
                var campaign = await _campaignService.RetryFailedAsync(id, request?.ProviderKey, cancellationToken);
                return Ok(ApiResponse<object>.SuccessResponse(
                    campaign, $"Retry complete — {campaign.FailedCount} still failing."));
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }
    }

    public class SendTestEmailRequest
    {
        public string ToEmail { get; set; } = string.Empty;
        public SendCampaignRequest Campaign { get; set; } = new();
    }

    public class RetryCampaignRequest
    {
        public string? ProviderKey { get; set; }
    }
}
