using Auth.Models.Constants;
using Auth.Models.Request.FLS;
using Auth.Models.Response;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers.FLS
{
    [Route("api/fls/meetings")]
    [ApiController]
    [Authorize]
    public class FLSMeetingController : ControllerBase
    {
        private readonly IFLSMeetingService _meetingService;
        private readonly IFLSSpeakerService _speakerService;
        private readonly ILogger<FLSMeetingController> _logger;

        public FLSMeetingController(
            IFLSMeetingService meetingService,
            IFLSSpeakerService speakerService,
            ILogger<FLSMeetingController> logger)
        {
            _meetingService = meetingService;
            _speakerService = speakerService;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        /// <summary>
        /// Get available meeting slots (unbooked, future).
        /// </summary>
        [Authorize(Roles = AppRoles.FlsSpeakerOrManagement)]
        [HttpGet("available")]
        public async Task<IActionResult> GetAvailable()
        {
            var slots = await _meetingService.GetAvailableSlotsAsync();
            return Ok(ApiResponse<object>.SuccessResponse(slots, "Available slots retrieved."));
        }

        /// <summary>
        /// Admin: get all meeting slots.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var slots = await _meetingService.GetAllSlotsAsync();
            return Ok(ApiResponse<object>.SuccessResponse(slots, "All slots retrieved."));
        }

        /// <summary>
        /// Get slots booked by the current speaker.
        /// </summary>
        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpGet("my")]
        public async Task<IActionResult> GetMy()
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var slots = await _meetingService.GetSlotsForSpeakerAsync(profile.Id);
            return Ok(ApiResponse<object>.SuccessResponse(slots, "Your meetings retrieved."));
        }

        /// <summary>
        /// Speaker books an available slot.
        /// </summary>
        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpPost("{slotId:int}/book")]
        public async Task<IActionResult> Book(int slotId, [FromBody] BookMeetingSlotRequest request)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var slot = await _meetingService.BookSlotAsync(slotId, profile.Id, request);
            return Ok(ApiResponse<object>.SuccessResponse(slot, "Meeting booked successfully."));
        }

        /// <summary>
        /// Speaker cancels their booking.
        /// </summary>
        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpPost("{slotId:int}/cancel")]
        public async Task<IActionResult> Cancel(int slotId)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            await _meetingService.CancelBookingAsync(slotId, profile.Id);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Booking cancelled."));
        }

        /// <summary>
        /// Admin: create a new meeting slot.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateMeetingSlotRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse<object>.ErrorResponse("Request body is required."));

            if (!ModelState.IsValid)
                return BadRequest(ApiResponse<object>.ErrorResponse("Invalid meeting slot data."));

            var slot = await _meetingService.CreateSlotAsync(request);
            return Ok(ApiResponse<object>.SuccessResponse(slot, "Meeting slot created."));
        }

        /// <summary>
        /// Admin: delete a meeting slot.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpDelete("{slotId:int}")]
        public async Task<IActionResult> Delete(int slotId)
        {
            await _meetingService.DeleteSlotAsync(slotId);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Meeting slot deleted."));
        }

        /// <summary>
        /// Admin: view all slots for a specific speaker.
        /// </summary>
        [Authorize(Roles = AppRoles.FlsManagement)]
        [HttpGet("speaker/{speakerProfileId:int}")]
        public async Task<IActionResult> GetForSpeaker(int speakerProfileId)
        {
            var slots = await _meetingService.GetSlotsForSpeakerAsync(speakerProfileId);
            return Ok(ApiResponse<object>.SuccessResponse(slots, ""));
        }
    }
}
