using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IAdminUserService _adminUserService;
        private readonly IAuditService _auditService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IAdminUserService adminUserService,
            IAuditService auditService,
            ILogger<AdminController> logger)
        {
            _adminUserService = adminUserService;
            _auditService = auditService;
            _logger = logger;
        }

        private string? GetCallerId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
        private string GetIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

        /// <summary>
        /// Get users with their roles (paginated). Defaults to page 1, 50 per page.
        /// Pass pageSize=200 to fetch all for small tenants.
        /// </summary>
        [HttpGet("users")]
        public async Task<ActionResult<ApiResponse<PagedResult<UserWithRolesResponse>>>> GetAllUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            var result = await _adminUserService.GetAllUsersAsync(page, pageSize);
            return Ok(ApiResponse<PagedResult<UserWithRolesResponse>>.SuccessResponse(result, "Users retrieved successfully"));
        }

        /// <summary>
        /// Update the roles of a specific user.
        /// </summary>
        [HttpPut("users/{userId}/roles")]
        public async Task<ActionResult<ApiResponse<bool>>> UpdateUserRoles(string userId, [FromBody] UpdateUserRolesRequest request)
        {
            if (request.Roles == null || !request.Roles.Any())
                return BadRequest(ApiResponse<bool>.ErrorResponse("Roles list cannot be empty"));

            var result = await _adminUserService.UpdateUserRolesAsync(userId, request.Roles);
            if (!result)
            {
                _logger.LogWarning("Failed to update roles for user {UserId}", userId);
                return NotFound(ApiResponse<bool>.ErrorResponse("User not found or role update failed"));
            }

            await _auditService.LogAsync(
                "Role.Updated",
                userId: GetCallerId(),
                payload: $"Target={userId} NewRoles=[{string.Join(",", request.Roles)}]",
                ipAddress: GetIp());

            return Ok(ApiResponse<bool>.SuccessResponse(true, "User roles updated successfully"));
        }

        [HttpPut("users/active")]
        public async Task<ActionResult<ApiResponse<bool>>> UpdateUsersActiveStatus([FromBody] UpdateUsersActiveStatusRequest request)
        {
            if (request.UserIds == null || !request.UserIds.Any())
                return BadRequest(ApiResponse<bool>.ErrorResponse("UserIds list cannot be empty"));

            var result = await _adminUserService.UpdateUsersActiveStatusAsync(request.UserIds, request.IsActive);
            if (!result)
            {
                _logger.LogWarning("Failed to update active status for users: {UserIds}", string.Join(", ", request.UserIds));
                return NotFound(ApiResponse<bool>.ErrorResponse("No users updated. Check IDs."));
            }

            await _auditService.LogAsync(
                "User.ActiveStatusUpdated",
                userId: GetCallerId(),
                payload: $"Targets=[{string.Join(",", request.UserIds)}] IsActive={request.IsActive}",
                ipAddress: GetIp());

            return Ok(ApiResponse<bool>.SuccessResponse(true, $"Active status updated for {request.UserIds.Count} users"));
        }
    }
}
