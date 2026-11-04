using Auth.Models.Constants;
using Auth.Models.DTOs.Email;
using Auth.Models.DTOs.Operations;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Email;
using Auth.Services.Interfaces.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    /// <summary>
    /// Operational visibility for admins and program managers: is the deployment healthy,
    /// and is it configured correctly.
    ///
    /// Read-only. Nothing here returns a secret value — the environment endpoint reports
    /// whether each tracked variable is set, never what it contains.
    /// </summary>
    [Route("api/operations")]
    [ApiController]
    [Authorize(Roles = AppRoles.Operations)]
    public class OperationsController : ControllerBase
    {
        private readonly IOperationsService _operationsService;
        private readonly IAuditQueryService _audit;
        private readonly IEmailDispatcher _emailDispatcher;
        private readonly IAuditService _auditService;
        private readonly ILogger<OperationsController> _logger;

        public OperationsController(
            IOperationsService operationsService,
            IAuditQueryService audit,
            IEmailDispatcher emailDispatcher,
            IAuditService auditService,
            ILogger<OperationsController> logger)
        {
            _operationsService = operationsService;
            _audit = audit;
            _emailDispatcher = emailDispatcher;
            _auditService = auditService;
            _logger = logger;
        }

        /// <summary>
        /// Sends a test email to the caller's own address.
        ///
        /// "Is email working?" otherwise has no answer short of asking a scholar to try
        /// forgot-password and report back. Only ever to the caller, so this cannot be used
        /// to send mail to anyone else. The provider's own error is returned on purpose: the
        /// only people who can call this are the ones who have to fix it.
        /// </summary>
        [HttpPost("email-test")]
        public async Task<ActionResult<ApiResponse<EmailSendResult>>> SendTestEmail(CancellationToken cancellationToken)
        {
            var to = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            if (string.IsNullOrWhiteSpace(to))
            {
                return BadRequest(ApiResponse<EmailSendResult>.ErrorResponse("Your account has no email address."));
            }

            var result = await _emailDispatcher.SendAsync(new OutboundEmail
            {
                ToEmail = to,
                Subject = "Test email from the Scholar Dashboard",
                TextBody = "If you can read this, the dashboard can send email. / Ako ovo čitate, platforma može slati email.",
                HtmlBody = "<p>If you can read this, the dashboard can send email.</p><p>Ako ovo čitate, platforma može slati email.</p>",
                Tag = "email-test"
            }, cancellationToken: cancellationToken);

            await _auditService.LogAsync(
                "Operations.EmailTest",
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                $"Provider={result.Provider} Success={result.Success}");

            return Ok(ApiResponse<EmailSendResult>.SuccessResponse(result, result.Success ? "Sent" : "Not sent"));
        }

        /// <summary>Deploy health: database, migrations, integrations, uptime.</summary>
        [HttpGet("health")]
        public async Task<ActionResult<ApiResponse<DeployHealthDto>>> GetHealth(CancellationToken cancellationToken)
        {
            var health = await _operationsService.GetHealthAsync(cancellationToken);
            return Ok(ApiResponse<DeployHealthDto>.SuccessResponse(health, "Health retrieved"));
        }

        /// <summary>
        /// Which tracked environment variables are set, and what breaks while they aren't.
        ///
        /// Returns presence only. There is intentionally no endpoint that returns values —
        /// not masked, not partial. If one is ever wanted for debugging, the right answer is
        /// to read them from the hosting platform, which has its own audit trail.
        /// </summary>
        [HttpGet("environment")]
        public ActionResult<ApiResponse<EnvironmentStatusDto>> GetEnvironment()
        {
            var status = _operationsService.GetEnvironmentStatus();
            return Ok(ApiResponse<EnvironmentStatusDto>.SuccessResponse(status, "Environment status retrieved"));
        }
        // ── Audit trail ───────────────────────────────────────────────────────

        /// <summary>
        /// The audit trail, filtered and paged.
        ///
        /// Read-only by design: audit rows are never edited or deleted through the
        /// application, or the trail would not be worth keeping.
        /// </summary>
        [HttpGet("audit")]
        public async Task<ActionResult<ApiResponse<PagedResult<AuditEventDto>>>> GetAudit(
            [FromQuery] AuditQuery query, CancellationToken cancellationToken)
        {
            var result = await _audit.SearchAsync(query, cancellationToken);
            return Ok(ApiResponse<PagedResult<AuditEventDto>>.SuccessResponse(result, "Audit events retrieved"));
        }

        /// <summary>Categories and the event types actually present, for the filter controls.</summary>
        [HttpGet("audit/filters")]
        public async Task<ActionResult<ApiResponse<AuditFilterOptionsDto>>> GetAuditFilters(
            CancellationToken cancellationToken)
        {
            var options = await _audit.GetFilterOptionsAsync(cancellationToken);
            return Ok(ApiResponse<AuditFilterOptionsDto>.SuccessResponse(options, "Filter options retrieved"));
        }
    }
}