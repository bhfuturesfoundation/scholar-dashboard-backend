using Auth.Models.Constants;
using Auth.Models.Request.FLS;
using Auth.Models.Response;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers.FLS
{
    [Route("api/fls/notifications")]
    [ApiController]
    [Authorize]
    public class FLSNotificationController : ControllerBase
    {
        private readonly IFLSNotificationService _notificationService;
        private readonly IFLSSpeakerService _speakerService;
        private readonly ILogger<FLSNotificationController> _logger;

        public FLSNotificationController(
            IFLSNotificationService notificationService,
            IFLSSpeakerService speakerService,
            ILogger<FLSNotificationController> logger)
        {
            _notificationService = notificationService;
            _speakerService = speakerService;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpGet]
        public async Task<IActionResult> GetNotifications([FromQuery] bool unreadOnly = false)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var notifications = await _notificationService.GetNotificationsAsync(profile.Id, unreadOnly);
            return Ok(ApiResponse<object>.SuccessResponse(notifications, ""));
        }

        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            var count = await _notificationService.GetUnreadCountAsync(profile.Id);
            return Ok(ApiResponse<object>.SuccessResponse(new { count }, ""));
        }

        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpPut("{notificationId:int}/read")]
        public async Task<IActionResult> MarkRead(int notificationId)
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            await _notificationService.MarkAsReadAsync(notificationId, profile.Id);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Marked as read."));
        }

        [Authorize(Roles = AppRoles.FLSSpeaker)]
        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            var profile = await _speakerService.GetProfileByUserIdAsync(GetUserId());
            if (profile == null)
                return NotFound(ApiResponse<object>.ErrorResponse("Speaker profile not found."));

            await _notificationService.MarkAllReadAsync(profile.Id);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "All notifications marked as read."));
        }

        [Authorize(Roles = AppRoles.FlsCommunications)]
        [HttpPost("send")]
        public async Task<IActionResult> Send([FromBody] SendNotificationRequest request)
        {
            await _notificationService.SendNotificationAsync(request);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Notification(s) sent."));
        }

        [Authorize(Roles = AppRoles.FlsCommunications)]
        [HttpPost("send-reminders")]
        public async Task<IActionResult> SendReminders()
        {
            await _notificationService.SendReminderEmailsAsync();
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Reminder notifications queued."));
        }
    }
}
