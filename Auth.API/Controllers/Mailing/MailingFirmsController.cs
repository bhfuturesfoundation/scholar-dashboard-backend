using Auth.Models.Constants;
using Auth.Models.DTOs.Mailing;
using Auth.Models.Enums.Mailing;
using Auth.Models.Request.Mailing;
using Auth.Models.Response;
using Auth.Services.Interfaces.Mailing;
using Auth.Services.Services.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers.Mailing
{
    /// <summary>
    /// The firm outreach directory: CRUD, bulk name detection, bulk categorisation, and
    /// spreadsheet import/export.
    /// </summary>
    [Route("api/mailing/firms")]
    [ApiController]
    [Authorize(Roles = AppRoles.Mailing)]
    public class MailingFirmsController : ControllerBase
    {
        private readonly IFirmDirectoryService _directory;
        private readonly IFirmImportExportService _importExport;
        private readonly ILogger<MailingFirmsController> _logger;

        public MailingFirmsController(
            IFirmDirectoryService directory,
            IFirmImportExportService importExport,
            ILogger<MailingFirmsController> logger)
        {
            _directory = directory;
            _importExport = importExport;
            _logger = logger;
        }

        private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

        private string UserName
        {
            get
            {
                var name = $"{User.FindFirstValue("FirstName")} {User.FindFirstValue("LastName")}".Trim();
                return name.Length > 0 ? name : User.FindFirstValue(ClaimTypes.Email) ?? "Unknown";
            }
        }

        [HttpGet]
        public async Task<ActionResult<ApiResponse<PagedResult<FirmDto>>>> Search(
            [FromQuery] FirmQuery query, CancellationToken cancellationToken)
        {
            var result = await _directory.SearchAsync(query, cancellationToken);
            return Ok(ApiResponse<PagedResult<FirmDto>>.SuccessResponse(result, "Firms retrieved"));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ApiResponse<FirmDto>>> Get(int id, CancellationToken cancellationToken)
        {
            var firm = await _directory.GetAsync(id, cancellationToken);

            return firm is null
                ? NotFound(ApiResponse<FirmDto>.ErrorResponse("Firm not found."))
                : Ok(ApiResponse<FirmDto>.SuccessResponse(firm, "Firm retrieved"));
        }

        [HttpPost]
        public async Task<ActionResult<ApiResponse<FirmDto>>> Create(
            [FromBody] UpsertFirmRequest request, CancellationToken cancellationToken)
        {
            var firm = await _directory.CreateAsync(request, UserId, cancellationToken);
            return Ok(ApiResponse<FirmDto>.SuccessResponse(firm, "Firm created"));
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<ApiResponse<FirmDto>>> Update(
            int id, [FromBody] UpsertFirmRequest request, CancellationToken cancellationToken)
        {
            var firm = await _directory.UpdateAsync(id, request, cancellationToken);
            return Ok(ApiResponse<FirmDto>.SuccessResponse(firm, "Firm updated"));
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id, CancellationToken cancellationToken)
        {
            await _directory.DeleteAsync(id, cancellationToken);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Firm removed"));
        }

        // ── Bulk operations ───────────────────────────────────────────────────

        /// <summary>
        /// Proposes contact names without saving. Two-step by design: the operator reviews
        /// every suggestion and its confidence before anything is written, because a wrong
        /// name goes to a real potential sponsor.
        /// </summary>
        [HttpPost("detect-names")]
        public async Task<ActionResult<ApiResponse<List<NameDetectionResultDto>>>> DetectNames(
            [FromBody] DetectNamesRequest request, CancellationToken cancellationToken)
        {
            var results = await _directory.DetectNamesAsync(request, cancellationToken);
            return Ok(ApiResponse<List<NameDetectionResultDto>>.SuccessResponse(
                results, $"{results.Count} firm(s) analysed"));
        }

        /// <summary>Saves the reviewed names from a detection run.</summary>
        [HttpPost("apply-names")]
        public async Task<ActionResult<ApiResponse<int>>> ApplyNames(
            [FromBody] ApplyNamesRequest request, CancellationToken cancellationToken)
        {
            var updated = await _directory.ApplyNamesAsync(request, cancellationToken);
            return Ok(ApiResponse<int>.SuccessResponse(updated, $"{updated} contact name(s) saved"));
        }

        [HttpPost("categorize")]
        public async Task<ActionResult<ApiResponse<int>>> Categorize(
            [FromBody] BulkCategorizeRequest request, CancellationToken cancellationToken)
        {
            var count = await _directory.BulkCategorizeAsync(request, cancellationToken);
            return Ok(ApiResponse<int>.SuccessResponse(count, $"{count} firm(s) categorised"));
        }

        [HttpPost("bulk-status")]
        public async Task<ActionResult<ApiResponse<int>>> BulkStatus(
            [FromBody] BulkStatusRequest request, CancellationToken cancellationToken)
        {
            var count = await _directory.BulkSetStatusAsync(request.FirmIds, request.Status, cancellationToken);
            return Ok(ApiResponse<int>.SuccessResponse(count, $"{count} firm(s) updated"));
        }

        // ── Import / export ───────────────────────────────────────────────────

        /// <summary>
        /// Imports a CSV or Excel file. Defaults to a dry run — the UI always validates
        /// first, because a bad spreadsheet is the most likely way this directory gets
        /// corrupted and "500 rows failed" is far cheaper to learn before the write.
        /// </summary>
        [HttpPost("import")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public async Task<ActionResult<ApiResponse<FirmImportResultDto>>> Import(
            IFormFile file,
            [FromQuery] bool dryRun = true,
            [FromQuery] bool updateExisting = true,
            [FromQuery] bool autoCategorize = true,
            [FromQuery] bool detectContactNames = true,
            [FromQuery] int? defaultFirmTypeId = null,
            CancellationToken cancellationToken = default)
        {
            if (file is null || file.Length == 0)
                return BadRequest(ApiResponse<object>.ErrorResponse("No file was uploaded."));

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".csv" or ".xlsx" or ".xlsm" or ".txt"))
            {
                return BadRequest(ApiResponse<object>.ErrorResponse(
                    "Upload a .csv or .xlsx file."));
            }

            await using var stream = file.OpenReadStream();

            var result = await _importExport.ImportAsync(
                stream,
                file.FileName,
                new FirmImportOptions
                {
                    DryRun = dryRun,
                    UpdateExisting = updateExisting,
                    AutoCategorize = autoCategorize,
                    DetectContactNames = detectContactNames,
                    DefaultFirmTypeId = defaultFirmTypeId
                },
                UserId,
                UserName,
                cancellationToken);

            var message = dryRun
                ? $"Validated {result.TotalRows} row(s) — nothing saved yet."
                : $"Imported {result.CreatedCount} new and updated {result.UpdatedCount} firm(s).";

            return Ok(ApiResponse<FirmImportResultDto>.SuccessResponse(result, message));
        }

        [HttpGet("export")]
        public async Task<IActionResult> Export(
            [FromQuery] int? firmTypeId,
            [FromQuery] int? firmGroupId,
            [FromQuery] FirmStatus? status,
            [FromQuery] string format = "xlsx",
            CancellationToken cancellationToken = default)
        {
            var table = await _importExport.BuildExportAsync(
                new FirmExportFilter { FirmTypeId = firmTypeId, FirmGroupId = firmGroupId, Status = status },
                cancellationToken);

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

            return format.Equals("csv", StringComparison.OrdinalIgnoreCase)
                ? File(TabularExporter.ToCsv(table), TabularExporter.CsvContentType, $"firms-{stamp}.csv")
                : File(TabularExporter.ToExcel(table), TabularExporter.ExcelContentType, $"firms-{stamp}.xlsx");
        }

        /// <summary>A blank file with the expected headers and example rows.</summary>
        [HttpGet("import-template")]
        public IActionResult ImportTemplate([FromQuery] string format = "xlsx")
        {
            var table = _importExport.BuildImportTemplate();

            return format.Equals("csv", StringComparison.OrdinalIgnoreCase)
                ? File(TabularExporter.ToCsv(table), TabularExporter.CsvContentType, "firm-import-template.csv")
                : File(TabularExporter.ToExcel(table), TabularExporter.ExcelContentType, "firm-import-template.xlsx");
        }
    }

    public class BulkStatusRequest
    {
        public List<int> FirmIds { get; set; } = new();
        public FirmStatus Status { get; set; }
    }
}
