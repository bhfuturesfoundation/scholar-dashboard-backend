using Auth.API.Extensions;
using Auth.API.Helpers;
using Auth.Models.Entities;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Web;

namespace Auth.API.Controllers
{
    /// <summary>
    /// Google and GitHub sign-in.
    ///
    /// The flow ends by redirecting back to the frontend with a token in the query string,
    /// because the API is stateless JWT and the browser is mid-redirect — there is no
    /// XHR response to put a body in. The frontend immediately stores the token and strips
    /// it from the URL so it doesn't linger in history or get copy-pasted.
    /// </summary>
    [Route("api/auth/external")]
    [ApiController]
    public class ExternalAuthController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly ITokenService _tokenService;
        private readonly IAuditService _audit;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ExternalAuthController> _logger;

        public ExternalAuthController(
            UserManager<User> userManager,
            ITokenService tokenService,
            IAuditService audit,
            IConfiguration configuration,
            ILogger<ExternalAuthController> logger)
        {
            _userManager = userManager;
            _tokenService = tokenService;
            _audit = audit;
            _configuration = configuration;
            _logger = logger;
        }

        private string FrontendBaseUrl =>
            (_configuration["FRONTEND_BASE_URL"] ?? "https://scholar-dashboard-frontend.vercel.app").TrimEnd('/');

        /// <summary>Which providers the login screen should show. Unauthenticated by design.</summary>
        [AllowAnonymous]
        [HttpGet("providers")]
        public ActionResult<ApiResponse<object>> GetProviders()
        {
            var providers = ExternalAuthExtensions.DescribeProviders(_configuration);

            return Ok(ApiResponse<object>.SuccessResponse(
                new
                {
                    providers = providers.Select(p => new { p.Key, p.DisplayName, p.IsConfigured, p.Hint }),
                    anyConfigured = providers.Any(p => p.IsConfigured)
                },
                "External providers retrieved"));
        }

        /// <summary>Starts the handshake. <paramref name="returnPath"/> is where to land afterwards.</summary>
        [AllowAnonymous]
        [HttpGet("{provider}")]
        public IActionResult Challenge(string provider, [FromQuery] string? returnPath = null)
        {
            var known = ExternalAuthExtensions.DescribeProviders(_configuration)
                .FirstOrDefault(p => p.Key.Equals(provider, StringComparison.OrdinalIgnoreCase));

            if (known is null || !known.IsConfigured)
                return Redirect($"{FrontendBaseUrl}/login?error={HttpUtility.UrlEncode($"{provider} sign-in is not available.")}");

            var properties = new AuthenticationProperties
            {
                RedirectUri = Url.Action(nameof(Callback), new { provider, returnPath }),
                Items = { ["provider"] = known.Key }
            };

            return Challenge(properties, known.Key);
        }

        [AllowAnonymous]
        [HttpGet("{provider}/complete")]
        public async Task<IActionResult> Callback(string provider, [FromQuery] string? returnPath = null)
        {
            var result = await HttpContext.AuthenticateAsync(ExternalAuthExtensions.ExternalScheme);

            // The correlation cookie is single-use; delete it whatever the outcome so a
            // failed attempt doesn't leave state behind.
            await HttpContext.SignOutAsync(ExternalAuthExtensions.ExternalScheme);

            if (!result.Succeeded || result.Principal is null)
            {
                _logger.LogWarning("External sign-in with {Provider} did not complete.", provider);
                return Fail("Sign-in was cancelled or failed. Please try again.");
            }

            var email = result.Principal.FindFirstValue(ClaimTypes.Email);

            if (string.IsNullOrWhiteSpace(email))
            {
                // GitHub returns nothing when the primary address is private. Say so
                // specifically — "sign-in failed" would send someone hunting the wrong thing.
                return Fail(
                    "That account did not share an email address. Make your email public with the " +
                    "provider, or sign in with your email and password.");
            }

            // Google tells us whether the address is verified. An unverified address must not
            // be trusted for account linking: anyone can register a provider account claiming
            // someone else's email, and linking on that basis is account takeover.
            var emailVerifiedClaim = result.Principal.FindFirstValue("email_verified");
            if (emailVerifiedClaim is not null &&
                bool.TryParse(emailVerifiedClaim, out var verified) && !verified)
            {
                return Fail("That provider account's email address is not verified.");
            }

            var user = await _userManager.FindByEmailAsync(email);

            if (user is null)
            {
                // Deliberately no auto-provisioning. Accounts here are created by the
                // programme team through scholar intake, with a role and a cohort — letting
                // anyone with a Google account create one would bypass all of that.
                _logger.LogInformation("External sign-in for unknown address {Email}.", email);

                await _audit.LogAsync("Auth.ExternalUnknownUser", payload: email,
                    ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

                return Fail("No account exists for that email address. Ask an administrator to add you first.");
            }

            if (!user.IsActive)
                return Fail("This account has been deactivated. Please contact an administrator.");

            // Signing in through a provider that verified the address proves ownership, so
            // this is a reasonable point to mark the email confirmed if it wasn't.
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }

            var token = await _tokenService.GenerateJwtTokenAsync(user);
            var refreshToken = await _tokenService.GenerateRefreshTokenAsync(
                user, HttpContext.Connection.RemoteIpAddress?.ToString());

            CookieHelper.SetRefreshTokenCookie(HttpContext, refreshToken);

            await _audit.LogAsync("Auth.ExternalLoginSuccess", user.Id, $"Provider={provider}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            var landing = string.IsNullOrWhiteSpace(returnPath) ? "/" : returnPath;

            // MustChangePassword users are sent to the change-password screen exactly as
            // they would be after a password login, so an OAuth route can't be used to skip it.
            if (user.MustChangePassword) landing = "/change-password";

            return Redirect(
                $"{FrontendBaseUrl}/auth/callback" +
                $"?token={HttpUtility.UrlEncode(token)}" +
                $"&redirect={HttpUtility.UrlEncode(landing)}");
        }

        private IActionResult Fail(string message) =>
            Redirect($"{FrontendBaseUrl}/login?error={HttpUtility.UrlEncode(message)}");
    }
}
