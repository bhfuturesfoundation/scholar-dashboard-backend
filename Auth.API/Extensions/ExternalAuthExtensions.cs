using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;

namespace Auth.API.Extensions
{
    /// <summary>Which external providers this deployment can offer, for the login screen.</summary>
    public record ExternalProviderStatus(string Key, string DisplayName, bool IsConfigured, string? Hint);

    /// <summary>
    /// Google and GitHub sign-in.
    ///
    /// Both are registered only when their credentials are present. Registering a scheme
    /// without a client id throws at startup, and an OAuth button that leads to a provider
    /// error page is worse than no button — so the API reports what is actually configured
    /// and the login screen renders only those.
    /// </summary>
    public static class ExternalAuthExtensions
    {
        public const string GoogleKey = "google";
        public const string GitHubKey = "github";

        /// <summary>
        /// Correlation cookie scheme for the OAuth handshake.
        ///
        /// External sign-in needs a cookie to carry state between the redirect out and the
        /// callback. The app is otherwise stateless JWT, so this is a dedicated short-lived
        /// scheme rather than reusing Identity's application cookie — it exists purely for
        /// the few seconds of the handshake and is deleted immediately after.
        /// </summary>
        public const string ExternalScheme = "External";

        public static IServiceCollection AddExternalAuthentication(
            this IServiceCollection services, IConfiguration configuration)
        {
            var googleId = configuration["GOOGLE_CLIENT_ID"];
            var googleSecret = configuration["GOOGLE_CLIENT_SECRET"];
            var githubId = configuration["GITHUB_CLIENT_ID"];
            var githubSecret = configuration["GITHUB_CLIENT_SECRET"];

            var anyConfigured =
                (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret)) ||
                (!string.IsNullOrWhiteSpace(githubId) && !string.IsNullOrWhiteSpace(githubSecret));

            if (!anyConfigured) return services;

            var builder = services.AddAuthentication();

            builder.AddCookie(ExternalScheme, options =>
            {
                // Cross-site: the browser is redirected from our API to the provider and back,
                // so the correlation cookie must survive a cross-site POST. That requires
                // SameSite=None, which in turn requires Secure.
                options.Cookie.Name = "Scholar.External";
                options.Cookie.SameSite = SameSiteMode.None;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.HttpOnly = true;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            });

            if (!string.IsNullOrWhiteSpace(googleId) && !string.IsNullOrWhiteSpace(googleSecret))
            {
                builder.AddGoogle(GoogleKey, options =>
                {
                    options.ClientId = googleId;
                    options.ClientSecret = googleSecret;
                    options.SignInScheme = ExternalScheme;
                    options.CallbackPath = "/api/auth/external/google/callback";

                    options.CorrelationCookie.SameSite = SameSiteMode.None;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;

                    // Needed to decide whether to trust the address for account linking.
                    options.ClaimActions.MapJsonKey("email_verified", "email_verified", "boolean");
                    options.Scope.Add("email");
                    options.Scope.Add("profile");
                });
            }

            if (!string.IsNullOrWhiteSpace(githubId) && !string.IsNullOrWhiteSpace(githubSecret))
            {
                builder.AddGitHub(GitHubKey, options =>
                {
                    options.ClientId = githubId;
                    options.ClientSecret = githubSecret;
                    options.SignInScheme = ExternalScheme;
                    options.CallbackPath = "/api/auth/external/github/callback";

                    options.CorrelationCookie.SameSite = SameSiteMode.None;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;

                    // GitHub only returns an address when this scope is requested, and even
                    // then only the primary one — accounts with a private email give nothing,
                    // which the callback has to handle.
                    options.Scope.Add("user:email");
                });
            }

            return services;
        }

        /// <summary>What the login screen should offer, and why a provider is unavailable.</summary>
        public static List<ExternalProviderStatus> DescribeProviders(IConfiguration configuration)
        {
            var googleReady = !string.IsNullOrWhiteSpace(configuration["GOOGLE_CLIENT_ID"])
                && !string.IsNullOrWhiteSpace(configuration["GOOGLE_CLIENT_SECRET"]);

            var githubReady = !string.IsNullOrWhiteSpace(configuration["GITHUB_CLIENT_ID"])
                && !string.IsNullOrWhiteSpace(configuration["GITHUB_CLIENT_SECRET"]);

            return new List<ExternalProviderStatus>
            {
                new(GoogleKey, "Google", googleReady,
                    googleReady ? null : "Set GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET."),
                new(GitHubKey, "GitHub", githubReady,
                    githubReady ? null : "Set GITHUB_CLIENT_ID and GITHUB_CLIENT_SECRET.")
            };
        }
    }
}
