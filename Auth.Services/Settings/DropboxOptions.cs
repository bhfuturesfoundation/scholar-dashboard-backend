namespace Auth.Services.Settings
{
    /// <summary>
    /// Dropbox credentials. See <c>docs/EMAIL_PROVIDERS.md</c> for how to obtain a refresh token.
    ///
    /// There is deliberately no AccessToken property. Dropbox access tokens expire after
    /// about four hours, so any deployment configured with one starts failing the same day.
    /// The app key, secret and refresh token do not expire, and the storage service mints
    /// access tokens from them on demand.
    /// </summary>
    public class DropboxOptions
    {
        /// <summary>DROPBOX_APP_KEY — Dropbox developer console → your app → Settings.</summary>
        public string? AppKey { get; set; }

        /// <summary>DROPBOX_APP_SECRET — same page.</summary>
        public string? AppSecret { get; set; }

        /// <summary>
        /// DROPBOX_REFRESH_TOKEN — obtained once via the OAuth2 offline flow. Does not expire,
        /// but is revoked if the app's access is removed in the Dropbox console.
        /// </summary>
        public string? RefreshToken { get; set; }
    }
}
