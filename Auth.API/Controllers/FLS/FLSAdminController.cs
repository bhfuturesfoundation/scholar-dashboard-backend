using Auth.Models.Constants;
using Auth.Models.Response;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers.FLS
{
    [Route("api/fls/admin")]
    [ApiController]
    [Authorize(Roles = AppRoles.FlsManagement)]
    public class FLSAdminController : ControllerBase
    {
        private readonly IFLSAdminService _adminService;
        private readonly ILogger<FLSAdminController> _logger;

        public FLSAdminController(IFLSAdminService adminService, ILogger<FLSAdminController> logger)
        {
            _adminService = adminService;
            _logger = logger;
        }

        /// <summary>
        /// Get overview of all speakers with completion status.
        /// Partner members need this to pick campaign recipients, so it is widened to the
        /// communications group; the write endpoints below stay management-only.
        /// </summary>
        [HttpGet("speakers")]
        [Authorize(Roles = AppRoles.FlsCommunications)]
        public async Task<IActionResult> GetAllSpeakers([FromQuery] bool includeDeregistered = false)
        {
            var speakers = await _adminService.GetAllSpeakersAsync(includeDeregistered);
            return Ok(ApiResponse<object>.SuccessResponse(speakers, $"Retrieved {speakers.Count} speakers."));
        }

        /// <summary>
        /// Get full profile details for one speaker.
        /// </summary>
        [HttpGet("speakers/{id:int}")]
        public async Task<IActionResult> GetSpeakerDetails(int id)
        {
            var profile = await _adminService.GetSpeakerDetailsAsync(id);
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker not found."));

            return Ok(ApiResponse<object>.SuccessResponse(profile, ""));
        }

        /// <summary>
        /// Export all speaker data as CSV.
        /// </summary>
        [HttpGet("speakers/export")]
        [Authorize(Roles = AppRoles.FlsCommunications)]
        public async Task<IActionResult> ExportSpeakers()
        {
            var csvBytes = await _adminService.ExportSpeakersAsCsvAsync();
            return File(csvBytes, "text/csv", $"fls-speakers-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        /// <summary>
        /// Get all presentation uploads pending verification.
        /// </summary>
        [HttpGet("uploads/pending")]
        public async Task<IActionResult> GetPendingUploads()
        {
            var uploads = await _adminService.GetPendingUploadsAsync();
            return Ok(ApiResponse<object>.SuccessResponse(uploads, $"{uploads.Count} pending upload(s)."));
        }
    }
}
