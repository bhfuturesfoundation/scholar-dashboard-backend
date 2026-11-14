using Auth.Models.DTOs.Admin;
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
        private readonly IAdminAccountService _adminAccountService;
        private readonly IAuditService _auditService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            IAdminUserService adminUserService,
            IAdminAccountService adminAccountService,
            IAuditService auditService,
            ILogger<AdminController> logger)
        {
            _adminUserService = adminUserService;
            _adminAccountService = adminAccountService;
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

        // ── Account help desk ───────────────────────────────────────────────────

        [HttpPost("users")]
        public async Task<ActionResult<ApiResponse<CreatedAccountDto>>> CreateUser(
            [FromBody] AdminCreateUserRequest request, CancellationToken ct) =>
            Ok(ApiResponse<CreatedAccountDto>.SuccessResponse(
                await _adminAccountService.CreateAccountAsync(GetCallerId()!, request, ct), "Account created"));

        [HttpGet("users/{userId}/account")]
        public async Task<ActionResult<ApiResponse<AdminAccountDetailDto>>> GetAccount(string userId, CancellationToken ct) =>
            Ok(ApiResponse<AdminAccountDetailDto>.SuccessResponse(
                await _adminAccountService.GetAccountAsync(userId, ct), "Account retrieved"));

        [HttpPut("users/{userId}/account")]
        public async Task<ActionResult<ApiResponse<AdminAccountDetailDto>>> UpdateAccount(
            string userId, [FromBody] AdminUpdateUserRequest request, CancellationToken ct) =>
            Ok(ApiResponse<AdminAccountDetailDto>.SuccessResponse(
                await _adminAccountService.UpdateAccountAsync(GetCallerId()!, userId, request, ct), "Account updated"));

        [HttpPost("users/{userId}/unlock")]
        public async Task<ActionResult<ApiResponse<bool>>> Unlock(string userId, CancellationToken ct)
        {
            await _adminAccountService.UnlockAsync(GetCallerId()!, userId, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Account unlocked"));
        }

        [HttpPost("users/{userId}/password-reset-email")]
        public async Task<ActionResult<ApiResponse<bool>>> SendPasswordResetEmail(string userId, CancellationToken ct)
        {
            try
            {
                await _adminAccountService.SendPasswordResetEmailAsync(GetCallerId()!, userId, ct);
                return Ok(ApiResponse<bool>.SuccessResponse(true, "Password reset email sent"));
            }
            catch (InvalidOperationException ex)
            {
                // The provider refused or is not configured. 502 rather than 500: our side
                // worked, the mail service did not — and the admin has a fallback to reach for.
                _logger.LogWarning(ex, "Admin password reset email for {UserId} failed.", userId);
                return StatusCode(StatusCodes.Status502BadGateway, ApiResponse<bool>.ErrorResponse(
                    "The email could not be sent. Set a temporary password instead."));
            }
        }

        [HttpPost("users/{userId}/temporary-password")]
        public async Task<ActionResult<ApiResponse<TemporaryPasswordDto>>> SetTemporaryPassword(string userId, CancellationToken ct) =>
            Ok(ApiResponse<TemporaryPasswordDto>.SuccessResponse(
                await _adminAccountService.SetTemporaryPasswordAsync(GetCallerId()!, userId, GetIp(), ct),
                "Temporary password set"));

        /// <summary>Permanent and irreversible. The web app asks the admin to type a confirmation first.</summary>
        [HttpPost("users/delete")]
        public async Task<ActionResult<ApiResponse<DeleteAccountsResultDto>>> DeleteUsers(
            [FromBody] DeleteAccountsRequest request, CancellationToken ct)
        {
            if (request.UserIds.Count == 0)
                return BadRequest(ApiResponse<DeleteAccountsResultDto>.ErrorResponse("Choose at least one account."));

            var result = await _adminAccountService.DeleteAccountsAsync(GetCallerId()!, request.UserIds, GetIp(), ct);
            return Ok(ApiResponse<DeleteAccountsResultDto>.SuccessResponse(result, $"Deleted {result.DeletedCount} account(s)"));
        }

        [HttpPost("users/{userId}/sign-out-everywhere")]
        public async Task<ActionResult<ApiResponse<int>>> SignOutEverywhere(string userId, CancellationToken ct) =>
            Ok(ApiResponse<int>.SuccessResponse(
                await _adminAccountService.SignOutEverywhereAsync(GetCallerId()!, userId, GetIp(), ct),
                "Signed out of all devices"));
    }
}
