using Auth.Models.Constants;
using Auth.Models.DTOs.Operations;
using Auth.Models.Response;
using Auth.Services.Interfaces.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        private readonly ILogger<OperationsController> _logger;

        public OperationsController(
            IOperationsService operationsService,
            IAuditQueryService audit,
            ILogger<OperationsController> logger)
        {
            _operationsService = operationsService;
            _audit = audit;
            _logger = logger;
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