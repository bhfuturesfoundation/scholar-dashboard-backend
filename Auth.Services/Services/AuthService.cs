using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserService _userService;
        private readonly ITokenService _tokenService;
        private readonly ILogger<IAuthService> _logger;
        private readonly IEmailService _emailService;
        private readonly ITwoFactorService _twoFactorService;

        public AuthService(
            IUserService userService,
            ITokenService tokenService,
            ILogger<IAuthService> logger,
            IEmailService emailService,
            ITwoFactorService twoFactorService)
        {
            _userService = userService;
            _tokenService = tokenService;
            _logger = logger;
            _emailService = emailService;
            _twoFactorService = twoFactorService;
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

            var (succeeded, user, requiresTwoFactor, emailConfirmed) = await _userService.VerifyCredentialsAsync(request.Email, request.Password);

            if (!succeeded)
            {
                _logger.LogWarning("Failed login attempt for email {Email} - invalid credentials", request.Email);
                throw new AuthenticationException("Invalid email or password.");
            }

            if (user.MustChangePassword)
            {
                _logger.LogInformation("User {Email} must change password before full login.", user.Email);
                var jwtPass = await _tokenService.GenerateJwtTokenAsync(user);
                var refreshPass = await _tokenService.GenerateRefreshTokenAsync(user, ipAddress);

                return new AuthResponse
                {
                    Token = jwtPass,
                    RefreshToken = refreshPass,
                    Expiration = DateTime.UtcNow.AddMinutes(60),
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
                Expiration = DateTime.UtcNow.AddMinutes(60),
                RequiresTwoFactor = false,
                EmailConfirmed = emailConfirmed
            };

            if (emailConfirmed)
            {
                _logger.LogInformation("Login successful for user {Email} with unconfirmed email", user.Email);
            }
            else
            {
                _logger.LogInformation("Login successful for user {Email}", user.Email);
            }

            _logger.LogInformation("Returning login response with refresh token: {RefreshToken}", response.RefreshToken);

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