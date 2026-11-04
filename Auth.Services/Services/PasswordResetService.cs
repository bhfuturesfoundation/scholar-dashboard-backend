using Auth.Models.Entities;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Auth.Services.Services
{
    /// <inheritdoc cref="IPasswordResetService"/>
    public class PasswordResetService : IPasswordResetService
    {
        private const string DefaultFrontendBaseUrl = "https://scholar-dashboard-frontend.vercel.app";

        private readonly UserManager<User> _userManager;
        private readonly IResendService _resendService;
        private readonly IConfiguration _configuration;

        public PasswordResetService(
            UserManager<User> userManager,
            IResendService resendService,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _resendService = resendService;
            _configuration = configuration;
        }

        public async Task SendResetLinkAsync(User user) =>
            await _resendService.SendEmailAsync(EmailOf(user), await BuildLinkAsync(user));

        public async Task SendWelcomeLinkAsync(User user) =>
            await _resendService.SendWelcomeEmailAsync(EmailOf(user), user.FirstName, await BuildLinkAsync(user));

        private async Task<string> BuildLinkAsync(User user) =>
            BuildResetLink(EmailOf(user), await _userManager.GeneratePasswordResetTokenAsync(user));

        private static string EmailOf(User user) =>
            string.IsNullOrWhiteSpace(user.Email)
                ? throw new InvalidOperationException("This account has no email address to send a link to.")
                : user.Email;

        /// <summary>
        /// Same query-string shape the reset page has always read (email + token), so links
        /// already sitting in inboxes keep working after this change.
        /// </summary>
        public string BuildResetLink(string email, string token)
        {
            var baseUrl = (_configuration["FRONTEND_BASE_URL"] is { Length: > 0 } configured
                ? configured
                : DefaultFrontendBaseUrl).TrimEnd('/');

            return $"{baseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        }
    }
}
