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
    /// Speaker profiles.
    ///
    /// Authorisation defaults to deny at the class level, with <c>[AllowAnonymous]</c>
    /// applied explicitly to the one public endpoint. Previously the class carried no
    /// [Authorize] at all and every endpoint protected itself — which worked, but meant a
    /// newly added action was public until someone remembered to annotate it. Deny-by-
    /// default turns that omission into a 401 instead of a data leak.
    /// </summary>
    [Route("api/fls/speakers")]
    [ApiController]
    [Authorize]
    public class FLSSpeakerController : ControllerBase
    {
        private readonly IFLSSpeakerService _speakerService;
        private readonly ILogger<FLSSpeakerController> _logger;

        public FLSSpeakerController(IFLSSpeakerService speakerService, ILogger<FLSSpeakerController> logger)
        {
            _speakerService = speakerService;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        /// <summary>
        /// Self-registration endpoint for FLS speakers. Intentionally public — this is how
        /// a speaker creates their account in the first place.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] FLSRegisterRequest request)
        {
            var profile = await _speakerService.RegisterSpeakerAsync(request);
            return Ok(ApiResponse<object>.SuccessResponse(profile, "Registration successful. Welcome to FLS Speaker Portal!"));
        }

        /// <summary>
        /// Get the current speaker's own profile.
        /// </summary>
        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpGet("me")]
        public async Task<IActionResult> GetMyProfile()
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            return Ok(ApiResponse<object>.SuccessResponse(profile, "Profile retrieved successfully."));
        }

        /// <summary>
        /// Speaker updates their own profile (bio, organization).
        /// </summary>
        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateSpeakerProfileRequest request)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var updated = await _speakerService.UpdateProfileAsync(profile.Id, request);
            return Ok(ApiResponse<object>.SuccessResponse(updated, "Profile updated successfully."));
        }

        /// <summary>
        /// Check if the speaker can still modify their uploads.
        /// </summary>
        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpGet("me/can-modify")]
        public async Task<IActionResult> CanModify()
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var canModify = await _speakerService.CanModifyUploadsAsync(profile.Id);
            return Ok(ApiResponse<object>.SuccessResponse(new { canModify, modificationDeadline = profile.ModificationDeadline }, ""));
        }

        /// <summary>
        /// Admin: get speaker by profile ID.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById(int id)
        {
            var profile = await _speakerService.GetProfileByIdAsync(id);
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            return Ok(ApiResponse<object>.SuccessResponse(profile, ""));
        }

        /// <summary>
        /// Admin: deregister a speaker.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpPost("{id:int}/deregister")]
        public async Task<IActionResult> Deregister(int id, [FromBody] DeregisterSpeakerRequest request)
        {
            await _speakerService.DeregisterSpeakerAsync(id, request, GetUserId());
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Speaker has been deregistered."));
        }

        /// <summary>
        /// Admin: reactivate a deregistered speaker.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpPost("{id:int}/reactivate")]
        public async Task<IActionResult> Reactivate(int id)
        {
            await _speakerService.ReactivateSpeakerAsync(id);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Speaker has been reactivated."));
        }

        /// <summary>
        /// Admin: update accommodation/flight status.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpPut("{id:int}/accommodation")]
        public async Task<IActionResult> UpdateAccommodation(int id, [FromBody] UpdateSpeakerAccommodationRequest request)
        {
            await _speakerService.UpdateAccommodationAsync(id, request);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Accommodation status updated."));
        }

        /// <summary>
        /// Admin: set modification deadline for a speaker.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpPut("{id:int}/modification-deadline")]
        public async Task<IActionResult> SetModificationDeadline(int id, [FromBody] SetModificationDeadlineRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse<object>.ErrorResponse("Request body is required."));

            await _speakerService.SetModificationDeadlineAsync(id, request);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Modification deadline updated."));
        }
    }
}
