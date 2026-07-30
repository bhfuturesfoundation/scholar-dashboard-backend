using Auth.Models.Constants;
using Auth.Models.Entities.Operations;
using Auth.Models.Enums.Operations;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Operations;
using Auth.Services.Services.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    /// <summary>
    /// Database backups and roster exports.
    ///
    /// Split by sensitivity rather than by resource: backups are Admin-only because they
    /// contain every scholar's journal entries and the full user table, while scholar
    /// exports are open to program managers because they contain only fields those roles
    /// already see on screen.
    /// </summary>
    [Route("api/operations")]
    [ApiController]
    [Authorize(Roles = AppRoles.Operations)]
    public class BackupController : ControllerBase
    {
        private readonly IBackupService _backupService;
        private readonly IScholarExportService _scholarExport;
        private readonly IAuditService _auditService;
        private readonly ILogger<BackupController> _logger;

        public BackupController(
            IBackupService backupService,
            IScholarExportService scholarExport,
            IAuditService auditService,
            ILogger<BackupController> logger)
        {
            _backupService = backupService;
            _scholarExport = scholarExport;
            _auditService = auditService;
            _logger = logger;
        }

        private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private string UserName =>
            $"{User.FindFirstValue("FirstName")} {User.FindFirstValue("LastName")}".Trim() is { Length: > 0 } n
                ? n
                : User.FindFirstValue(ClaimTypes.Email) ?? "Unknown";

        // ── Backups (Admin only) ──────────────────────────────────────────────

        /// <summary>Which backup formats this deployment can produce.</summary>
        [Authorize(Roles = AppRoles.BackupManagement)]
        [HttpGet("backups/formats")]
        public async Task<ActionResult<ApiResponse<List<BackupFormatAvailability>>>> GetFormats(
            CancellationToken cancellationToken)
        {
            var formats = await _backupService.GetFormatAvailabilityAsync(cancellationToken);
            return Ok(ApiResponse<List<BackupFormatAvailability>>.SuccessResponse(formats, "Formats retrieved"));
        }

        [Authorize(Roles = AppRoles.BackupManagement)]
        [HttpGet("backups")]
        public async Task<ActionResult<ApiResponse<List<BackupRecord>>>> GetHistory(
            [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
        {
            var history = await _backupService.GetHistoryAsync(limit, cancellationToken);
            return Ok(ApiResponse<List<BackupRecord>>.SuccessResponse(history, "Backup history retrieved"));
        }

        /// <summary>
        /// Produces a backup and returns it as a download.
        ///
        /// Audited unconditionally, including who asked and whether credentials were
        /// included — this is the single most sensitive action in the system, and an
        /// unexplained backup in the history is exactly the signal worth investigating.
        /// </summary>
        [Authorize(Roles = AppRoles.BackupManagement)]
        [HttpPost("backups")]
        public async Task<IActionResult> CreateBackup(
            [FromBody] CreateBackupRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var artifact = await _backupService.CreateAsync(request, UserId, UserName, cancellationToken);

                await _auditService.LogAsync(
                    "Backup.Created",
                    userId: UserId,
                    payload: $"Format={request.Format} Sensitive={request.IncludeSensitiveData} " +
                             $"Size={artifact.Content.Length} File={artifact.Record.FileName}",
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

                return File(artifact.Content, artifact.ContentType, artifact.Record.FileName);
            }
            catch (InvalidOperationException ex)
            {
                // Format genuinely unavailable in this environment (pg_dump missing, version
                // mismatch). A 400 with the real reason beats a 500 with none.
                _logger.LogWarning(ex, "Backup request rejected.");
                return BadRequest(ApiResponse<object>.ErrorResponse(ex.Message));
            }
        }

        [Authorize(Roles = AppRoles.BackupManagement)]
        [HttpPost("backups/prune")]
        public async Task<ActionResult<ApiResponse<int>>> Prune(CancellationToken cancellationToken)
        {
            var pruned = await _backupService.PruneExpiredAsync(cancellationToken);

            await _auditService.LogAsync("Backup.Pruned", userId: UserId, payload: $"Removed={pruned}");

            return Ok(ApiResponse<int>.SuccessResponse(pruned, $"{pruned} expired backup record(s) removed"));
        }

        // ── Scholar export (Admin + ProgramManager) ───────────────────────────

        /// <summary>
        /// Exports the scholar roster as CSV or Excel.
        /// Defaults to active scholars only; deactivated accounts are opt-in.
        /// </summary>
        [HttpGet("exports/scholars")]
        public async Task<IActionResult> ExportScholars(
            [FromQuery] ScholarInclusion include = ScholarInclusion.ActiveOnly,
            [FromQuery] string format = "xlsx",
            CancellationToken cancellationToken = default)
        {
            var table = await _scholarExport.BuildAsync(new ScholarExportFilter { Include = include }, cancellationToken);

            await _auditService.LogAsync(
                "Export.Scholars",
                userId: UserId,
                payload: $"Include={include} Format={format} Rows={table.Rows.Count}");

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                return File(TabularExporter.ToCsv(table),
                    TabularExporter.CsvContentType, $"scholars-{stamp}.csv");
            }

            return File(TabularExporter.ToExcel(table),
                TabularExporter.ExcelContentType, $"scholars-{stamp}.xlsx");
        }
    }
}
