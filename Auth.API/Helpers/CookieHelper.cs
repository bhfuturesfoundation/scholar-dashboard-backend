namespace Auth.API.Helpers
{
    public static class CookieHelper
    {
        private const string RefreshTokenCookieName = "refresh_token";

        public static void SetRefreshTokenCookie(HttpContext context, string refreshToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTime.UtcNow.AddDays(7),
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            };

            context.Response.Cookies.Append(RefreshTokenCookieName, refreshToken, cookieOptions);
        }

        public static void DeleteRefreshTokenCookie(HttpContext context)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/"
            };

            context.Response.Cookies.Delete(RefreshTokenCookieName, cookieOptions);
        }
    }
}
