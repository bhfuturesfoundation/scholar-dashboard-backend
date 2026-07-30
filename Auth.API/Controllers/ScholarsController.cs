using Auth.Models.Constants;
using Auth.Models.DTOs.Scholars;
using Auth.Models.Enums.Scholars;
using Auth.Models.Request.Scholars;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Scholars;
using Auth.Services.Services.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    /// <summary>
    /// Scholar generations, cohort promotion and bulk intake.
    ///
    /// Open to program managers as well as admins: this is the programme team's day job.
    /// Every mutating action is audited, and the two destructive ones — promotion and
    /// import — are preview-then-apply.
    /// </summary>
    [Route("api/scholars")]
    [ApiController]
    [Authorize(Roles = AppRoles.JournalOversight)]
    public class ScholarsController : ControllerBase
    {
        private readonly IScholarLifecycleService _lifecycle;
        private readonly IMentorAssignmentService _mentors;
        private readonly IAuditService _audit;
        private readonly ILogger<ScholarsController> _logger;

        public ScholarsController(
            IScholarLifecycleService lifecycle,
            IMentorAssignmentService mentors,
            IAuditService audit,
            ILogger<ScholarsController> logger)
        {
            _lifecycle = lifecycle;
            _mentors = mentors;
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

        private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

        // ── Overview ──────────────────────────────────────────────────────────

        [HttpGet("overview")]
        public async Task<ActionResult<ApiResponse<ScholarOverviewDto>>> GetOverview(CancellationToken ct) =>
            Ok(ApiResponse<ScholarOverviewDto>.SuccessResponse(await _lifecycle.GetOverviewAsync(ct), "Overview retrieved"));

        // ── Generations ───────────────────────────────────────────────────────

        [HttpGet("generations")]
        public async Task<ActionResult<ApiResponse<List<ScholarGenerationDto>>>> GetGenerations(CancellationToken ct) =>
            Ok(ApiResponse<List<ScholarGenerationDto>>.SuccessResponse(
                await _lifecycle.GetGenerationsAsync(ct), "Generations retrieved"));

        [HttpPost("generations")]
        public async Task<ActionResult<ApiResponse<ScholarGenerationDto>>> CreateGeneration(
            [FromBody] UpsertGenerationRequest request, CancellationToken ct)
        {
            var generation = await _lifecycle.CreateGenerationAsync(request, UserId, ct);
            await _audit.LogAsync("Scholars.GenerationCreated", UserId, $"{generation.Name} ({generation.Year})", Ip);
            return Ok(ApiResponse<ScholarGenerationDto>.SuccessResponse(generation, "Generation created"));
        }

        [HttpPut("generations/{id:int}")]
        public async Task<ActionResult<ApiResponse<ScholarGenerationDto>>> UpdateGeneration(
            int id, [FromBody] UpsertGenerationRequest request, CancellationToken ct) =>
            Ok(ApiResponse<ScholarGenerationDto>.SuccessResponse(
                await _lifecycle.UpdateGenerationAsync(id, request, ct), "Generation updated"));

        [HttpPost("generations/{id:int}/current")]
        public async Task<ActionResult<ApiResponse<bool>>> SetCurrent(int id, CancellationToken ct)
        {
            await _lifecycle.SetCurrentGenerationAsync(id, ct);
            await _audit.LogAsync("Scholars.CurrentGenerationChanged", UserId, $"Generation={id}", Ip);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Current generation updated"));
        }

        [HttpDelete("generations/{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteGeneration(int id, CancellationToken ct)
        {
            await _lifecycle.DeleteGenerationAsync(id, ct);
            await _audit.LogAsync("Scholars.GenerationDeleted", UserId, $"Generation={id}", Ip);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Generation deleted"));
        }

        // ── Promotion ─────────────────────────────────────────────────────────

        /// <summary>Who would move, without moving them. The UI requires this before applying.</summary>
        [HttpPost("promotions/preview")]
        public async Task<ActionResult<ApiResponse<PromotionPreviewDto>>> PreviewPromotion(
            [FromBody] PromotionRequest request, CancellationToken ct) =>
            Ok(ApiResponse<PromotionPreviewDto>.SuccessResponse(
                await _lifecycle.PreviewPromotionAsync(request, ct), "Preview generated"));

        [HttpPost("promotions")]
        public async Task<ActionResult<ApiResponse<PromotionResultDto>>> ApplyPromotion(
            [FromBody] PromotionRequest request, CancellationToken ct)
        {
            var result = await _lifecycle.ApplyPromotionAsync(request, UserId, UserName, ct);

            await _audit.LogAsync(
                "Scholars.Promoted",
                UserId,
                $"Step={request.Step} Generation={request.GenerationId?.ToString() ?? "all"} " +
                $"Affected={result.AffectedCount} Deactivated={request.DeactivateAlumni} Batch={result.BatchId}",
                Ip);

            return Ok(ApiResponse<PromotionResultDto>.SuccessResponse(result, result.Message));
        }

        [HttpGet("promotions")]
        public async Task<ActionResult<ApiResponse<List<PromotionBatchDto>>>> GetPromotionHistory(
            [FromQuery] int limit = 25, CancellationToken ct = default) =>
            Ok(ApiResponse<List<PromotionBatchDto>>.SuccessResponse(
                await _lifecycle.GetPromotionHistoryAsync(limit, ct), "History retrieved"));

        [HttpPost("promotions/{id:int}/revert")]
        public async Task<ActionResult<ApiResponse<PromotionResultDto>>> RevertPromotion(int id, CancellationToken ct)
        {
            var result = await _lifecycle.RevertPromotionAsync(id, UserId, ct);
            await _audit.LogAsync("Scholars.PromotionReverted", UserId, $"Batch={id} Restored={result.AffectedCount}", Ip);
            return Ok(ApiResponse<PromotionResultDto>.SuccessResponse(result, result.Message));
        }

        [HttpPost("status")]
        public async Task<ActionResult<ApiResponse<int>>> SetStatus(
            [FromBody] SetScholarStatusRequest request, CancellationToken ct)
        {
            var count = await _lifecycle.SetStatusAsync(request.UserIds, request.Status, request.GenerationId, ct);
            await _audit.LogAsync("Scholars.StatusSet", UserId, $"Status={request.Status} Count={count}", Ip);
            return Ok(ApiResponse<int>.SuccessResponse(count, $"{count} scholar(s) updated"));
        }

        // ── Bulk intake ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates scholar accounts from a spreadsheet. Dry-run by default.
        ///
        /// A committed run returns the generated passwords once. They are not recoverable
        /// afterwards — only the hash is stored — so the response is the single opportunity
        /// to capture them.
        /// </summary>
        [HttpPost("import")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public async Task<ActionResult<ApiResponse<ScholarImportResultDto>>> Import(
            IFormFile file,
            [FromQuery] bool dryRun = true,
            [FromQuery] int? generationId = null,
            [FromQuery] ScholarStatus status = ScholarStatus.Junior,
            [FromQuery] bool archiveCredentials = true,
            CancellationToken ct = default)
        {
            if (file is null || file.Length == 0)
                return BadRequest(ApiResponse<object>.ErrorResponse("No file was uploaded."));

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".csv" or ".xlsx" or ".xlsm" or ".txt"))
                return BadRequest(ApiResponse<object>.ErrorResponse("Upload a .csv or .xlsx file."));

            await using var stream = file.OpenReadStream();

            var result = await _lifecycle.ImportScholarsAsync(
                stream,
                file.FileName,
                new ScholarImportOptions
                {
                    DryRun = dryRun,
                    GenerationId = generationId,
                    Status = status,
                    ArchiveCredentials = archiveCredentials
                },
                UserId,
                ct);

            if (!dryRun)
            {
                await _audit.LogAsync(
                    "Scholars.Imported",
                    UserId,
                    $"File={file.FileName} Created={result.CreatedCount} Generation={result.GenerationName ?? "none"}",
                    Ip);
            }

            var message = dryRun
                ? $"Validated {result.TotalRows} row(s) — no accounts created."
                : $"Created {result.CreatedCount} scholar account(s).";

            return Ok(ApiResponse<ScholarImportResultDto>.SuccessResponse(result, message));
        }

        [HttpGet("import-template")]
        public IActionResult ImportTemplate([FromQuery] string format = "xlsx")
        {
            var table = _lifecycle.BuildImportTemplate();

            return format.Equals("csv", StringComparison.OrdinalIgnoreCase)
                ? File(TabularExporter.ToCsv(table), TabularExporter.CsvContentType, "scholar-import-template.csv")
                : File(TabularExporter.ToExcel(table), TabularExporter.ExcelContentType, "scholar-import-template.xlsx");
        }
        // ── Mentor assignment ─────────────────────────────────────────────────

        [HttpGet("mentors/overview")]
        public async Task<ActionResult<ApiResponse<MentorAssignmentOverviewDto>>> MentorOverview(CancellationToken ct) =>
            Ok(ApiResponse<MentorAssignmentOverviewDto>.SuccessResponse(
                await _mentors.GetOverviewAsync(ct), "Overview retrieved"));

        [HttpGet("mentors")]
        public async Task<ActionResult<ApiResponse<List<MentorSummaryDto>>>> GetMentors(CancellationToken ct) =>
            Ok(ApiResponse<List<MentorSummaryDto>>.SuccessResponse(
                await _mentors.GetMentorsAsync(ct), "Mentors retrieved"));

        /// <summary>Scholars and their mentor. Filter to the unassigned to work through the gap.</summary>
        [HttpGet("mentors/scholars")]
        public async Task<ActionResult<ApiResponse<List<MenteeAssignmentDto>>>> GetMenteeAssignments(
            [FromQuery] bool onlyUnassigned = false,
            [FromQuery] string? search = null,
            CancellationToken ct = default) =>
            Ok(ApiResponse<List<MenteeAssignmentDto>>.SuccessResponse(
                await _mentors.GetScholarsAsync(onlyUnassigned, search, ct), "Scholars retrieved"));

        [HttpPost("mentors/assign")]
        public async Task<ActionResult<ApiResponse<bool>>> AssignMentor(
            [FromBody] AssignMentorRequest request, CancellationToken ct)
        {
            await _mentors.AssignAsync(request.ScholarId, request.MentorId, ct);
            await _audit.LogAsync("Scholars.MentorAssigned", UserId,
                $"Scholar={request.ScholarId} Mentor={request.MentorId}", Ip);

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Mentor assigned"));
        }

        [HttpPost("mentors/unassign/{scholarId}")]
        public async Task<ActionResult<ApiResponse<bool>>> UnassignMentor(string scholarId, CancellationToken ct)
        {
            await _mentors.UnassignAsync(scholarId, ct);
            await _audit.LogAsync("Scholars.MentorUnassigned", UserId, $"Scholar={scholarId}", Ip);

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Mentor removed"));
        }

        /// <summary>
        /// Pairs scholars to mentors from a spreadsheet. Dry-run by default.
        ///
        /// Rows that cannot be paired come back as issues rather than being logged and
        /// forgotten — this replaces the startup seeder whose failures nobody saw.
        /// </summary>
        [HttpPost("mentors/import")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public async Task<ActionResult<ApiResponse<MentorPairingResultDto>>> ImportPairings(
            IFormFile file,
            [FromQuery] bool dryRun = true,
            [FromQuery] bool reassignExisting = false,
            CancellationToken ct = default)
        {
            if (file is null || file.Length == 0)
                return BadRequest(ApiResponse<object>.ErrorResponse("No file was uploaded."));

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".csv" or ".xlsx" or ".xlsm" or ".txt"))
                return BadRequest(ApiResponse<object>.ErrorResponse("Upload a .csv or .xlsx file."));

            await using var stream = file.OpenReadStream();

            var result = await _mentors.ImportPairingsAsync(
                stream, file.FileName,
                new MentorPairingOptions { DryRun = dryRun, ReassignExisting = reassignExisting },
                ct);

            if (!dryRun)
            {
                await _audit.LogAsync("Scholars.MentorsPaired", UserId,
                    $"File={file.FileName} Assigned={result.AssignedCount} Reassigned={result.ReassignedCount}", Ip);
            }

            var message = dryRun
                ? $"Validated {result.TotalRows} row(s) — nothing saved."
                : $"{result.AssignedCount} assigned, {result.ReassignedCount} reassigned.";

            return Ok(ApiResponse<MentorPairingResultDto>.SuccessResponse(result, message));
        }
    }

    public class AssignMentorRequest
    {
        public string ScholarId { get; set; } = string.Empty;
        public string MentorId { get; set; } = string.Empty;
    }
}