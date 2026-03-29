using Auth.Models.Enums.FLS;
using Auth.Models.Request.FLS;
using Auth.Models.Response;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers.FLS
{
    [Route("api/fls/uploads")]
    [ApiController]
    [Authorize]
    public class FLSUploadController : ControllerBase
    {
        private readonly IFLSUploadService _uploadService;
        private readonly IFLSSpeakerService _speakerService;
        private readonly ILogger<FLSUploadController> _logger;

        public FLSUploadController(
            IFLSUploadService uploadService,
            IFLSSpeakerService speakerService,
            ILogger<FLSUploadController> logger)
        {
            _uploadService = uploadService;
            _speakerService = speakerService;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        /// <summary>
        /// Speaker uploads a file. Type determined by query param.
        /// </summary>
        [Authorize(Roles = "FLSSpeaker")]
        [HttpPost]
        [RequestSizeLimit(11 * 1024 * 1024)] // 11 MB max
        public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromQuery] UploadType uploadType)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            if (profile.IsDeregistered)
                return BadRequest(ApiResponse<object>.ErrorResponse("Deregistered speakers cannot upload files."));

            var canModify = await _speakerService.CanModifyUploadsAsync(profile.Id);
            if (!canModify)
                return BadRequest(ApiResponse<object>.ErrorResponse("The modification deadline has passed. You can no longer update uploads."));

            var result = await _uploadService.UploadFileAsync(profile.Id, file, uploadType);
            return Ok(ApiResponse<object>.SuccessResponse(result, $"{uploadType} uploaded successfully."));
        }

        /// <summary>
        /// Get all uploads for the current speaker.
        /// </summary>
        [Authorize(Roles = "FLSSpeaker")]
        [HttpGet("my")]
        public async Task<IActionResult> GetMyUploads()
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var uploads = await _uploadService.GetUploadsForSpeakerAsync(profile.Id);
            return Ok(ApiResponse<object>.SuccessResponse(uploads, "Uploads retrieved."));
        }

        /// <summary>
        /// Get completion status for current speaker.
        /// </summary>
        [Authorize(Roles = "FLSSpeaker")]
        [HttpGet("my/status")]
        public async Task<IActionResult> GetMyStatus()
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var status = await _uploadService.GetCompletionStatusAsync(profile.Id);
            return Ok(ApiResponse<object>.SuccessResponse(status, ""));
        }

        /// <summary>
        /// Download a specific upload file (authenticated only).
        /// </summary>
        [HttpGet("{uploadId:int}/download")]
        public async Task<IActionResult> Download(int uploadId)
        {
            var (fileBytes, contentType, fileName) = await _uploadService.DownloadFileAsync(uploadId);
            return File(fileBytes, contentType, fileName);
        }

        /// <summary>
        /// Speaker deletes their own upload (within modification window).
        /// </summary>
        [Authorize(Roles = "FLSSpeaker")]
        [HttpDelete("{uploadId:int}")]
        public async Task<IActionResult> Delete(int uploadId)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var canModify = await _speakerService.CanModifyUploadsAsync(profile.Id);
            if (!canModify)
                return BadRequest(ApiResponse<object>.ErrorResponse("The modification deadline has passed."));

            await _uploadService.DeleteUploadAsync(uploadId, profile.Id);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Upload deleted."));
        }

        /// <summary>
        /// Admin: verify/approve/reject a presentation upload.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpPut("{uploadId:int}/verify")]
        public async Task<IActionResult> VerifyUpload(int uploadId, [FromBody] VerifyUploadRequest request)
        {
            var result = await _uploadService.VerifyUploadAsync(uploadId, request, GetUserId());
            return Ok(ApiResponse<object>.SuccessResponse(result, $"Upload status updated to {request.Status}."));
        }

        /// <summary>
        /// Admin: get all uploads for a specific speaker.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpGet("speaker/{speakerProfileId:int}")]
        public async Task<IActionResult> GetForSpeaker(int speakerProfileId)
        {
            var uploads = await _uploadService.GetUploadsForSpeakerAsync(speakerProfileId);
            return Ok(ApiResponse<object>.SuccessResponse(uploads, ""));
        }

        /// <summary>
        /// Admin: get completion status for a specific speaker.
        /// </summary>
        [Authorize(Roles = "Admin,FLSAdmin")]
        [HttpGet("speaker/{speakerProfileId:int}/status")]
        public async Task<IActionResult> GetStatusForSpeaker(int speakerProfileId)
        {
            var status = await _uploadService.GetCompletionStatusAsync(speakerProfileId);
            return Ok(ApiResponse<object>.SuccessResponse(status, ""));
        }
    }
}
