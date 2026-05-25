using Dropbox.Api;
using Dropbox.Api.Files;
using System.Text.Json;

/// <summary>
/// Uploads files to Dropbox using the OAuth2 offline (refresh-token) flow.
///
/// Required Railway environment variables (set once, never expire):
///   DROPBOX_APP_KEY      — from https://www.dropbox.com/developers/apps → your app → Settings
///   DROPBOX_APP_SECRET   — same page
///   DROPBOX_REFRESH_TOKEN — one-time setup, see README for how to obtain
///
/// Why not DROPBOX_ACCESS_TOKEN?
///   Short-lived access tokens expire in ~4 hours. The refresh token is permanent
///   and lets this code mint a fresh access token on every upload automatically.
/// </summary>
public static class DropboxUploader
{
    public static async Task UploadTextAsync(string dropboxPath, string content)
    {
        var accessToken = await GetFreshAccessTokenAsync();

        using var dbx = new DropboxClient(accessToken);
        using var mem = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await dbx.Files.UploadAsync(
            dropboxPath,
            WriteMode.Overwrite.Instance,
            body: mem
        );
    }

    /// <summary>
    /// Exchanges the permanent refresh token for a short-lived access token.
    /// This call is cheap (~100 ms) and always produces a valid token.
    /// </summary>
    private static async Task<string> GetFreshAccessTokenAsync()
    {
        var appKey = Environment.GetEnvironmentVariable("DROPBOX_APP_KEY")
            ?? throw new InvalidOperationException("Missing DROPBOX_APP_KEY environment variable.");
        var appSecret = Environment.GetEnvironmentVariable("DROPBOX_APP_SECRET")
            ?? throw new InvalidOperationException("Missing DROPBOX_APP_SECRET environment variable.");
        var refreshToken = Environment.GetEnvironmentVariable("DROPBOX_REFRESH_TOKEN")
            ?? throw new InvalidOperationException("Missing DROPBOX_REFRESH_TOKEN environment variable.");

        using var http = new HttpClient();
        var response = await http.PostAsync(
            "https://api.dropboxapi.com/oauth2/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = appKey,
                ["client_secret"] = appSecret
            }));

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Dropbox token endpoint returned no access_token.");
    }
}


