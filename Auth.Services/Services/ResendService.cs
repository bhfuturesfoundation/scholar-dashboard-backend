using Auth.Models.DTOs.Email;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Email;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    /// <summary>
    /// Password-reset email sender.
    ///
    /// Previously this class talked to EmailJS directly and read its credentials in the
    /// constructor, throwing if they were absent. Because it is registered as a singleton,
    /// a missing EMAILJS_* variable meant the very first password-reset request blew up
    /// with a DI resolution error instead of a handled failure — and it hard-wired the app
    /// to one vendor. It now delegates to <see cref="IEmailDispatcher"/>, so password
    /// resets use whichever provider is configured and degrade gracefully when none is.
    ///
    /// The name and <see cref="IResendService"/> interface are kept so existing callers
    /// (AuthController) are unaffected.
    /// </summary>
    public class ResendEmailService : IResendService
    {
        private readonly IEmailDispatcher _dispatcher;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly ILogger<ResendEmailService> _logger;

        private const string ResetSubject = "Reset your BH Futures Foundation password";

        private const string ResetBody = """
            Hi,

            We received a request to reset the password for your BH Futures Foundation account.

            Open this link to choose a new password:
            {{resetLink}}

            The link is single-use and expires shortly. If you didn't request a reset you can
            safely ignore this email — your password will stay as it is.

            BH Futures Foundation
            """;

        private const string WelcomeSubject = "Dobro došli / Welcome to the BH Futures Foundation dashboard";

        // Bosnian first: almost every recipient reads it first, and nobody has picked a
        // language yet when this arrives.
        private const string WelcomeBody = """
            Zdravo {{firstName}},

            Napravljen vam je račun na platformi BH Futures Foundation.
            Otvorite ovaj link i izaberite svoju lozinku:
            {{link}}

            Link važi 24 sata. Ako istekne, na stranici za prijavu kliknite "Zaboravljena lozinka?"
            i upišite ovu email adresu.

            ---

            Hi {{firstName}},

            An account has been created for you on the BH Futures Foundation dashboard.
            Open this link to choose your password:
            {{link}}

            The link works for 24 hours. If it expires, click "Forgot password?" on the sign-in
            page and enter this email address.

            BH Futures Foundation
            """;

        public ResendEmailService(
            IEmailDispatcher dispatcher,
            IEmailTemplateRenderer renderer,
            ILogger<ResendEmailService> logger)
        {
            _dispatcher = dispatcher;
            _renderer = renderer;
            _logger = logger;
        }

        public Task SendWelcomeEmailAsync(string to, string? firstName, string setPasswordLink) =>
            SendAsync(to, WelcomeSubject, WelcomeBody, "welcome", new Dictionary<string, string?>
            {
                ["firstName"] = string.IsNullOrWhiteSpace(firstName) ? "" : firstName,
                ["link"] = setPasswordLink
            });

        public Task SendEmailAsync(string to, string resetLink) =>
            SendAsync(to, ResetSubject, ResetBody, "password-reset",
                new Dictionary<string, string?> { ["resetLink"] = resetLink });

        private async Task SendAsync(
            string to, string subject, string body, string tag, Dictionary<string, string?> variables)
        {
            var rendered = _renderer.Render(subject, body, variables);

            var result = await _dispatcher.SendAsync(new OutboundEmail
            {
                ToEmail = to,
                Subject = rendered.Subject,
                HtmlBody = rendered.HtmlBody,
                TextBody = rendered.TextBody,
                Tag = tag
            });

            if (!result.Success)
            {
                _logger.LogError(
                    "{Tag} email to {Email} failed via {Provider}: {Error}",
                    tag, to, result.Provider, result.Error);

                // Surface as an exception so AuthController's existing catch reports the
                // failure to the caller rather than claiming the email was sent.
                throw new InvalidOperationException($"Failed to send {tag} email: {result.Error}");
            }
        }
    }
}
