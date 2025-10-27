using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly IAdminUserService _adminUserService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IAdminUserService adminUserService,
            ILogger<AdminController> logger)
        {
            _adminUserService = adminUserService;
            _logger = logger;
        }

        /// <summary>
        /// Get all users with their roles.
        /// </summary>
        [HttpGet("users")]
        public async Task<ActionResult<ApiResponse<List<UserWithRolesResponse>>>> GetAllUsers()
        {
            var users = await _adminUserService.GetAllUsersAsync();
            return Ok(ApiResponse<List<UserWithRolesResponse>>.SuccessResponse(users, "All users retrieved successfully"));
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

            return Ok(ApiResponse<bool>.SuccessResponse(true, $"Active status updated for {request.UserIds.Count} users"));
        }
    }
}
