using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Models.Results;
using Auth.Services.Interfaces;
using Auth.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Services.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserService _userService;
        private readonly ITokenService _tokenService;
        private readonly ILogger<IAuthService> _logger;
        private readonly IEmailService _emailService;
        private readonly ITwoFactorService _twoFactorService;
        private readonly JWTSettings _jwtSettings;

        public AuthService(
            IUserService userService,
            ITokenService tokenService,
            ILogger<IAuthService> logger,
            IEmailService emailService,
            ITwoFactorService twoFactorService,
            IOptions<JWTSettings> jwtSettings)
        {
            _userService = userService;
            _tokenService = tokenService;
            _logger = logger;
            _emailService = emailService;
            _twoFactorService = twoFactorService;
            _jwtSettings = jwtSettings.Value;
        }

        public async Task<(User User, RegisterResponse Response)> RegisterAsync(RegisterRequest request)
        {
            _logger.LogInformation("Starting registration for email {Email}", request.Email);

            var user = await _userService.CreateUserAsync(request);

            var response = new RegisterResponse
            {
                UserId = user.Id,
                Email = user.Email,
                RequiresEmailConfirmation = true,
                RequiresPasswordChange = true,
            };

            _logger.LogInformation("Registration successful for user {Email}", user.Email);
            return (user, response);
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request, string ipAddress = null)
        {
            _logger.LogInformation("Starting login for email {Email}", request.Email);

            var verification = await _userService.VerifyCredentialsAsync(request.Email, request.Password);

            if (!verification.Succeeded)
            {
                // Each reason gets its own message. Previously every failure surfaced to the
                // browser as 400 "Login failed.", so a locked-out account, a deactivated
                // account and a typo were indistinguishable — including to us in support.
                throw verification.FailureReason switch
                {
                    CredentialFailureReason.LockedOut => new AuthenticationException(
                        verification.LockoutEnd.HasValue
                            ? $"Too many failed attempts. Try again after {verification.LockoutEnd.Value.UtcDateTime:HH:mm} UTC."
                            : "Too many failed attempts. Please try again shortly."),

                    CredentialFailureReason.Disabled => new AuthenticationException(
                        "This account has been deactivated. Please contact an administrator."),

                    _ => new AuthenticationException("Invalid email or password.")
                };
            }

            var user = verification.User!;
            var requiresTwoFactor = verification.RequiresTwoFactor;
            var emailConfirmed = verification.EmailConfirmed;

            if (user.MustChangePassword)
            {
                _logger.LogInformation("User {Email} must change password before full login.", user.Email);
                var jwtPass = await _tokenService.GenerateJwtTokenAsync(user);
                var refreshPass = await _tokenService.GenerateRefreshTokenAsync(user, ipAddress);

                return new AuthResponse
                {
                    Token = jwtPass,
                    RefreshToken = refreshPass,
                    // Was hardcoded to 60 while tokens are actually issued for
                    // JWTSettings.ExpirationInMinutes (480), so the client was told the
                    // session ended seven hours before it did.
                    Expiration = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes),
                    RequiresPasswordChange = true,
                    RequiresTwoFactor = false,
                    EmailConfirmed = emailConfirmed
                };
            }

            if (requiresTwoFactor)
            {
                _logger.LogInformation("Login requires 2FA for user {Email}", request.Email);

                var code = await _twoFactorService.GenerateTwoFactorCodeAsync(user.Id);

                _emailService.Queue2FACodeAsync(user.Email, code);

                return new AuthResponse
                {
                    RequiresTwoFactor = true,
                    EmailConfirmed = emailConfirmed,
                    RefreshToken = string.Empty
                };
            }

            var jwtToken = await _tokenService.GenerateJwtTokenAsync(user);
            var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user, ipAddress);

            var response = new AuthResponse
            {
                Token = jwtToken,
                RefreshToken = refreshToken,
                Expiration = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes),
                RequiresTwoFactor = false,
                EmailConfirmed = emailConfirmed
            };

            // The two branches here were inverted along with the flag they tested, so the
            // "unconfirmed email" notice was shown to exactly the users whose email WAS
            // confirmed.
            if (emailConfirmed)
                _logger.LogInformation("Login successful for user {Email}", user.Email);
            else
                _logger.LogInformation("Login successful for user {Email} with unconfirmed email", user.Email);

            // Never log the refresh token — it is a bearer credential, and application logs
            // are retained and shipped far more widely than credentials should be.
            _logger.LogInformation("Returning login response for user {Email}", user.Email);

            return response;
        }

        public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request, string ipAddress = null)
        {
            _logger.LogInformation("Starting token refresh");

            try
            {
                var user = await _tokenService.ValidateRefreshTokenAsync(request.Token, request.RefreshToken, ipAddress);

                var result = await _tokenService.RotateRefreshTokenAsync(
                    request.Token, request.RefreshToken, user.Id, ipAddress);

                _logger.LogInformation("Token successfully refreshed for user {Email}", user.Email);

                return new AuthResponse
                {
                    Token = result.Token,
                    RefreshToken = result.RefreshToken,
                    Expiration = result.Expiration,
                    RequiresTwoFactor = false,
                    EmailConfirmed = user.EmailConfirmed
                };
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Security exception during token refresh: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred while refreshing token");
                throw;
            }
        }

        public async Task<bool> LogoutAsync(string userId, string refreshToken = null)
        {
            _logger.LogInformation("Starting logout for user {UserId}", userId);

            try
            {
                if (!string.IsNullOrEmpty(refreshToken))
                {
                    await _tokenService.RevokeRefreshTokenAsync(refreshToken, userId);
                    _logger.LogInformation("Specific refresh token revoked for user {UserId}", userId);
                }
                else
                {
                    await _tokenService.RevokeAllRefreshTokensAsync(userId);
                    _logger.LogInformation("All refresh tokens revoked for user {UserId}", userId);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred while logging out user {UserId}", userId);
                throw;
            }
        }

        public async Task RequestPasswordResetAsync(ForgotPasswordRequest request)
        {
            var user = await _userService.GetByEmailAsync(request.Email);
            if (user == null)
            {
                // Do not reveal user existence
                return;
            }

            // Generate token
            var token = await _userService.GeneratePasswordResetTokenAsync(user);

            // Encode token for URL
            var tokenEncoded = System.Web.HttpUtility.UrlEncode(token);

            // Build reset link (frontend will handle /reset-password page)
            var resetLink = $"https://yourfrontend.com/reset-password?email={user.Email}&token={tokenEncoded}";

            // Send email
            await _emailService.SendEmailAsync(user.Email, "Reset your password",
                $"<p>Click <a href='{resetLink}'>here</a> to reset your password. This link is valid for 1 hour.</p>");
        }
        public async Task ResetPasswordAsync(ResetPasswordRequest request)
        {
            var user = await _userService.GetByEmailAsync(request.Email);
            if (user == null)
                throw new Exception("Invalid request");

            var result = await _userService.ResetPasswordAsync(user, request.Token, request.NewPassword);

            if (!result.Succeeded)
                throw new Exception("Failed to reset password");

            // Optional: force user to login again
        }

    }
}