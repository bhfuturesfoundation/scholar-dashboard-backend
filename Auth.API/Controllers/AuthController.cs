using Auth.API.Helpers;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Auth.Models.DTOs.Account;
using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using System.Security.Claims;
using System.Text;

[Route("api/auth")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IResendService _resendService;
    private readonly IUserService _userService;
    private readonly ITwoFactorService _twoFactorService;
    private readonly IEmailService _emailService;
    private readonly IAuditService _auditService;
    private readonly IAccountService _accountService;
    private readonly IAvatarService _avatarService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        IResendService resendService,
        IUserService userService,
        ITwoFactorService twoFactorService,
        IEmailService emailService,
        IAuditService auditService,
        IAccountService accountService,
        IAvatarService avatarService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _resendService = resendService;
        _userService = userService;
        _twoFactorService = twoFactorService;
        _emailService = emailService;
        _auditService = auditService;
        _accountService = accountService;
        _avatarService = avatarService;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private string GetIpAddress() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";


    // ── Self-service account ──────────────────────────────────────────────────
    //
    // Everything here is scoped to the caller's own id from the token, never a route or
    // body parameter. Before these existed the only thing anyone could do to their own
    // account was change their password: a misspelled surname needed a staff email, and
    // signing in on a shared machine could not be undone.

    /// <summary>Everything the settings screen shows, in one round trip.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<AccountOverviewDto>>> GetAccount(CancellationToken ct) =>
        Ok(ApiResponse<AccountOverviewDto>.SuccessResponse(
            await _accountService.GetOverviewAsync(GetUserId(), ct), "Account retrieved"));

    /// <summary>
    /// Updates the caller's own name. Email, title, roles and scholar status are
    /// deliberately not editable here — see UpdateProfileRequest.
    /// </summary>
    [Authorize]
    [HttpPut("me")]
    public async Task<ActionResult<ApiResponse<AccountOverviewDto>>> UpdateAccount(
        [FromBody] UpdateProfileRequest request, CancellationToken ct) =>
        Ok(ApiResponse<AccountOverviewDto>.SuccessResponse(
            await _accountService.UpdateProfileAsync(GetUserId(), request, ct), "Profile updated"));

    /// <summary>
    /// Revokes every refresh token for this account, including the caller's own.
    ///
    /// The current access token stays valid until it expires — revoking issued JWTs would
    /// need a denylist checked on every request, which is a real cost for a rare action.
    /// Every other device is locked out within the access-token lifetime and cannot renew.
    /// </summary>
    [Authorize]
    [HttpPost("me/sign-out-everywhere")]
    public async Task<ActionResult<ApiResponse<int>>> SignOutEverywhere(CancellationToken ct) =>
        Ok(ApiResponse<int>.SuccessResponse(
            await _accountService.SignOutEverywhereAsync(GetUserId(), GetIpAddress(), ct),
            "Signed out of all devices"));

    /// <summary>
    /// A copy of everything this account holds, as JSON.
    ///
    /// Journal entries are personal reflections written in confidence, which is exactly
    /// why this takes no user id: it can only ever return the caller's own.
    /// </summary>
    [Authorize]
    [HttpGet("me/export")]
    public async Task<IActionResult> ExportOwnData(CancellationToken ct)
    {
        var bytes = await _accountService.ExportOwnDataAsync(GetUserId(), ct);
        var fileName = $"my-data-{DateTime.UtcNow:yyyy-MM-dd}.json";

        return File(bytes, "application/json", fileName);
    }

    // ── Avatars ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Uploads a new profile picture, replacing any existing one.
    ///
    /// Returns the refreshed overview rather than 204, so the client picks up the new
    /// <c>AvatarUpdatedAt</c> in the same round trip — that timestamp is the cache-buster,
    /// and a client that had to guess it would render the previous image.
    /// </summary>
    [Authorize]
    [HttpPost("me/avatar")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<AccountOverviewDto>>> UploadAvatar(
        IFormFile file, CancellationToken ct)
    {
        // 6 MB at the framework, 5 MB in the service. Deliberately not the same number: the
        // framework limit covers the whole multipart body — boundaries, headers, filename —
        // so setting it to exactly 5 MB would reject a 5 MB file for its envelope and produce
        // a bare 413 instead of the service's explanatory message.
        await _avatarService.UploadAsync(GetUserId(), file, ct);

        return Ok(ApiResponse<AccountOverviewDto>.SuccessResponse(
            await _accountService.GetOverviewAsync(GetUserId(), ct), "Avatar updated"));
    }

    /// <summary>Removes the caller's profile picture. Succeeds even if there was none.</summary>
    [Authorize]
    [HttpDelete("me/avatar")]
    public async Task<ActionResult<ApiResponse<AccountOverviewDto>>> DeleteAvatar(CancellationToken ct)
    {
        await _avatarService.DeleteAsync(GetUserId(), ct);

        return Ok(ApiResponse<AccountOverviewDto>.SuccessResponse(
            await _accountService.GetOverviewAsync(GetUserId(), ct), "Avatar removed"));
    }

    /// <summary>
    /// Serves any signed-in user's avatar.
    ///
    /// Takes a user id and is not scoped to the caller, unlike everything in the /me group
    /// above — deliberately, because an avatar's entire purpose is to appear beside *other*
    /// people's names: the presence list, the kudos feed, the admin roster. [Authorize] with
    /// no role is the right boundary: nothing here is private among signed-in members, and it
    /// must not be readable by the open internet.
    ///
    /// Raw bytes rather than the usual ApiResponse envelope, because the consumer is an
    /// &lt;img src&gt; and the browser needs an image, not JSON with base64 in it.
    /// </summary>
    [Authorize]
    [HttpGet("/api/users/{id}/avatar")]
    public async Task<IActionResult> GetUserAvatar(string id, CancellationToken ct)
    {
        var avatar = await _avatarService.GetAsync(id, ct);

        // 404 rather than a placeholder image. The client already renders an initials
        // monogram for people with no picture, and serving a generated default here would
        // mean every avatar-less scholar costs a request that returns something the client
        // then has to detect and discard.
        if (avatar is null) return NotFound();

        var etag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{avatar.ETag}\"");

        // ── The 304 path is the point of this endpoint ────────────────────────
        //
        // Without it, a 30-person roster is 30 image downloads on every render — the presence
        // dropdown alone reopens several times a session, and the browser will happily
        // re-request each one. Answering If-None-Match with an empty 304 turns that into 30
        // header exchanges of a few hundred bytes each, and it costs one string comparison
        // here because the hash is stored on the row rather than computed from the bytes.
        //
        // Compared before the body is touched, which is the only ordering that actually saves
        // anything: checking after loading the image would still have read every byte.
        var requested = Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(requested) && requested.Contains(avatar.ETag, StringComparison.Ordinal))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers.ETag = etag.ToString();

        // Private, not public: a shared proxy has no business holding one member's picture to
        // hand to another client. Short max-age with must-revalidate rather than a long one,
        // because this URL is stable per user — the freshness that matters comes from the
        // ETag revalidation above, and from the ?v= cache-buster the client appends when the
        // picture actually changes.
        Response.Headers.CacheControl = "private, max-age=300, must-revalidate";

        return File(avatar.Bytes, avatar.ContentType);
    }

    [EnableRateLimiting("auth-email")]
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<RegisterResponse>>> Register([FromBody] RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request);

        var confirmationToken = await _userService.GenerateEmailConfirmationTokenAsync(result.User.Id);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(confirmationToken));

        var callbackUrl = $"{Request.Scheme}://{Request.Host}/api/auth/confirm-email?userId={result.User.Id}&token={encodedToken}";
        _emailService.QueueEmailConfirmationAsync(result.User.Email, callbackUrl);

        var loginResult = await _authService.LoginAsync(new LoginRequest
        {
            Email = request.Email,
            Password = request.Password
        });

        if (!string.IsNullOrEmpty(loginResult.RefreshToken))
            CookieHelper.SetRefreshTokenCookie(HttpContext, loginResult.RefreshToken);

        // Keep RefreshToken null in response so client doesn�t accidentally expose it
        loginResult.RefreshToken = null;

        return Ok(ApiResponse<RegisterResponse>.SuccessResponse(
            new RegisterResponse
            {
                UserId = result.User.Id,
                Email = result.User.Email,
                RequiresEmailConfirmation = true,
                Token = loginResult.Token,
                Expiration = loginResult.Expiration,
                RequiresPasswordChange = true
            },
            "Registration successful. Please check your email to confirm your account. You're now logged in."));
    }

    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string userId, string token)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
        {
            return Content(EmailConfirmationBuilder.GetErrorHtml(
                "Invalid email confirmation link. The link appears to be missing required information."),
                "text/html");
        }

        try
        {
            var decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            var result = await _userService.ConfirmEmailAsync(userId, decodedToken);

            return Content(
                result
                    ? EmailConfirmationBuilder.GetSuccessHtml()
                    : EmailConfirmationBuilder.GetErrorHtml("We couldn't confirm your email. The verification link may have expired or was already used."),
                "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming email for user {UserId}", userId);
            return Content(EmailConfirmationBuilder.GetErrorHtml(
                "An error occurred while trying to confirm your email. Please try again later."),
                "text/html");
        }
    }

    [EnableRateLimiting("ip-only")]
    [HttpPost("resend-confirmation-email")]
    public async Task<IActionResult> ResendConfirmationEmail([FromBody] ResendConfirmationEmailRequest request)
    {
        var user = await _userService.GetUserByEmailAsync(request.Email);

        if (user == null || user.EmailConfirmed)
        {
            return Ok(ApiResponse<bool>.SuccessResponse(true,
                user?.EmailConfirmed == true
                    ? "Your email address is already confirmed."
                    : "If your email address exists in our system, a confirmation email has been sent."));
        }

        var confirmationToken = await _userService.GenerateEmailConfirmationTokenAsync(user.Id);
        var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(confirmationToken));
        var callbackUrl = $"{Request.Scheme}://{Request.Host}/api/auth/confirm-email?userId={user.Id}&token={encodedToken}";

        _emailService.QueueEmailConfirmationAsync(user.Email, callbackUrl);

        return Ok(ApiResponse<bool>.SuccessResponse(true,
            "A confirmation email has been sent. Please check your inbox."));
    }

    [EnableRateLimiting("auth-email")]
    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login([FromBody] LoginRequest request)
    {
        string ipAddress = GetIpAddress();
        _logger.LogInformation("Login request from IP: {IpAddress}", ipAddress);

        try
        {
            var result = await _authService.LoginAsync(request, ipAddress);

            if (!string.IsNullOrEmpty(result.RefreshToken))
                CookieHelper.SetRefreshTokenCookie(HttpContext, result.RefreshToken);

            string message = "Login successful";

            if (!result.EmailConfirmed)
                message = "Login successful. Note: Your email is not yet confirmed. Some features may be limited.";
            else if (result.RequiresTwoFactor)
                message = "2FA verification required";

            // Fire-and-forget audit — don't await so it can't slow the login response
            _ = _auditService.LogAsync("Login.Success", payload: request.Email, ipAddress: ipAddress);

            // Hide refresh token in response payload, keep it only in cookie for security
            result.RefreshToken = null;

            return Ok(ApiResponse<AuthResponse>.SuccessResponse(result, message));
        }
        catch (AppExceptions)
        {
            // Expected failures (bad credentials, lockout, deactivated account) carry their
            // own message and status via ErrorHandlingMiddleware. This used to be swallowed
            // into a blanket 400 "Login failed.", which hid the reason from the user and from
            // us — a locked-out account and a typo looked identical in the browser.
            _ = _auditService.LogAsync("Login.Failed", payload: request.Email, ipAddress: ipAddress);
            throw;
        }
        catch (Exception ex)
        {
            // Genuinely unexpected — a 500 is the honest answer, not a 400.
            _logger.LogError(ex, "Unexpected error during login for {Email}", request.Email);
            _ = _auditService.LogAsync("Login.Error", payload: request.Email, ipAddress: ipAddress);
            throw;
        }
    }

    [Authorize]
    [HttpGet("current-user/title")]
    public async Task<ActionResult<ApiResponse<string>>> GetCurrentUserTitle()
    {
        var title = await _userService.GetUserTitleAsync(GetUserId());
        return Ok(ApiResponse<string>.SuccessResponse(title, "User Title"));
    }


    [Authorize]
    [HttpGet("current-user")]
    public async Task<ActionResult<ApiResponse<CurrentUserResponse>>> GetCurrentUser()
    {
        var user = await _userService.GetCurrentUserAsync(GetUserId());
        return Ok(ApiResponse<CurrentUserResponse>.SuccessResponse(user, "User Data"));
    }

    [Authorize]
    [HttpGet("members/search")]
    public async Task<ActionResult<ApiResponse<List<MemberSearchResponse>>>> SearchMembers([FromQuery] string query, [FromQuery] int limit = 8)
    {
        var results = await _userService.SearchMembersAsync(query, limit, GetUserId());
        return Ok(ApiResponse<List<MemberSearchResponse>>.SuccessResponse(results, "Members search results"));
    }

    [Authorize]
    [EnableRateLimiting("email-only")]
    [HttpPost("setup-2fa")]
    public async Task<ActionResult<ApiResponse<bool>>> SetupTwoFactor()
    {
        var result = await _twoFactorService.SetupTwoFactorAsync(GetUserId());
        return Ok(ApiResponse<bool>.SuccessResponse(result, "2FA enabled"));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(ApiResponse<AuthResponse>.ErrorResponse("No refresh token present."));

        try
        {
            var expiredToken = Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last() ?? string.Empty;

            var result = await _authService.RefreshTokenAsync(
                new RefreshTokenRequest { Token = expiredToken, RefreshToken = refreshToken },
                GetIpAddress());

            CookieHelper.SetRefreshTokenCookie(HttpContext, result.RefreshToken);
            result.RefreshToken = null;

            return Ok(ApiResponse<AuthResponse>.SuccessResponse(result, "Token refreshed"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token refresh failed");
            return Unauthorized(ApiResponse<AuthResponse>.ErrorResponse("Session expired. Please log in again."));
        }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<ActionResult<ApiResponse<bool>>> Logout()
    {
        var refreshToken = Request.Cookies["refresh_token"];
        var result = await _authService.LogoutAsync(GetUserId(), refreshToken);

        CookieHelper.DeleteRefreshTokenCookie(HttpContext);

        return Ok(ApiResponse<bool>.SuccessResponse(result, "Logout successful"));
    }

    [Authorize]
    [EnableRateLimiting("email-only")]
    [HttpGet("generate-2fa-code")]
    public async Task<ActionResult<ApiResponse<string>>> GenerateTwoFactorCode()
    {
        string userId = GetUserId();
        string userEmail = await _userService.GetUserEmailByIdAsync(userId);
        string code = await _twoFactorService.GenerateTwoFactorCodeAsync(userId);

        _emailService.Queue2FACodeAsync(userEmail, code);

        return Ok(ApiResponse<string>.SuccessResponse(
            "Check your email for the verification code",
            "A verification code has been sent to your email address. The code will expire in 15 minutes."
        ));
    }

    [EnableRateLimiting("auth-email")]
    [HttpPost("two-factor")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> TwoFactorVerify([FromBody] TwoFactorRequest request)
    {
        var result = await _twoFactorService.ValidateTwoFactorAsync(request);

        if (!string.IsNullOrEmpty(result.RefreshToken))
            CookieHelper.SetRefreshTokenCookie(HttpContext, result.RefreshToken);

        // Hide refresh token from client response
        result.RefreshToken = null;

        return Ok(ApiResponse<AuthResponse>.SuccessResponse(result, "2FA verification successful"));
    }
    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse<bool>>> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        try
        {
            var userId = GetUserId();

            await _userService.ChangePasswordAsync(
                userId: userId,
                currentPassword: request.CurrentPassword,
                newPassword: request.NewPassword
            );

            _logger.LogInformation("User {UserId} changed their password successfully", userId);

            // ? return success (frontend will log them out)
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Password changed successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to change password for user {UserId}", GetUserId());
            return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to change password. " + ex.Message));
        }
    }
    [HttpPost("forgot-password")]
    public async Task<ActionResult<ApiResponse<bool>>> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        try
        {
            var user = await _userService.GetByEmailAsync(request.Email);
            if (user == null)
            {
                _logger.LogInformation("Forgot password requested for non-existent email: {Email}", request.Email);
                return Ok(ApiResponse<bool>.SuccessResponse(true, "If this email exists, a reset link has been sent."));
            }

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                _logger.LogWarning("User exists but email is empty: {UserId}", user.Id);
                return BadRequest(ApiResponse<bool>.ErrorResponse("Cannot send email: user email is empty."));
            }

            var token = await _userService.GeneratePasswordResetTokenAsync(user);
            var tokenEncoded = System.Web.HttpUtility.UrlEncode(token);

            var resetLink = $"https://scholar-dashboard-frontend.vercel.app/reset-password?email={user.Email}&token={tokenEncoded}";

            // Send using existing EmailJS service with link injected
            await _resendService.SendEmailAsync(
                user.Email,
                resetLink
            );

            return Ok(ApiResponse<bool>.SuccessResponse(true, "If this email exists, a reset link has been sent."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send password reset email for {Email}", request.Email);
            return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to send password reset email. " + ex.Message));
        }
    }


    [HttpPost("reset-password")]
    public async Task<ActionResult<ApiResponse<bool>>> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            var user = await _userService.GetByEmailAsync(request.Email);
            if (user == null)
                return BadRequest(ApiResponse<bool>.ErrorResponse("Invalid reset request."));

            var result = await _userService.ResetPasswordAsync(user, request.Token, request.NewPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                return BadRequest(ApiResponse<bool>.ErrorResponse(errors));
            }

            user.MustChangePassword = false;
            await _userService.UpdateUserAsync(user);

            _ = _auditService.LogAsync("Password.Reset", payload: request.Email, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(ApiResponse<bool>.SuccessResponse(true, "Password has been reset successfully."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset password for {Email}", request.Email);
            return BadRequest(ApiResponse<bool>.ErrorResponse("Failed to reset password. " + ex.Message));
        }
    }
}

