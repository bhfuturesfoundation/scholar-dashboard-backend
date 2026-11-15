namespace Auth.Services.Settings
{
    public class RefreshTokenSettings
    {
        public int ExpirationInDays { get; set; }
        public int MaxRefreshCount { get; set; }
        public int MaxActiveSessionsPerUser { get; set; }
        public bool EnableTokenRotation { get; set; }
        public bool DetectTokenReuse { get; set; }

        /// <summary>
        /// How long after a normal rotation the superseded refresh token is still accepted.
        ///
        /// This exists because a browser fires several requests at once on page load. When
        /// the access token has expired, each one presents the same refresh token from the
        /// cookie; the first rotates it and the rest arrive holding a value that is now
        /// revoked-and-replaced — indistinguishable from an attacker replaying a stolen
        /// token unless we allow for the overlap. Without this window, reuse detection
        /// revoked every session on every page load.
        ///
        /// Kept short: long enough to cover concurrent in-flight requests, far too short to
        /// be useful to someone replaying a token captured earlier.
        /// </summary>
        public int RotationGraceSeconds { get; set; } = 60;

        /// <summary>
        /// Longest a sign-in can last, however often it is refreshed. Rotation used to give
        /// each new refresh token a fresh <see cref="ExpirationInDays"/>, so an always-open
        /// tab never expired (one account renewed itself every few hours for a month).
        /// <see cref="MaxRefreshCount"/> was meant to stop that but is counted per token,
        /// and every rotated token starts again at zero.
        /// </summary>
        public int AbsoluteSessionLifetimeDays { get; set; } = 30;
    }
}