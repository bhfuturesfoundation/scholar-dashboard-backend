using Auth.Services.Interfaces.Storage;
using Auth.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Auth.Services.Services.Storage
{
    /// <summary>
    /// Dropbox client using the OAuth2 offline (refresh-token) flow, with the access token
    /// cached until shortly before it expires.
    ///
    /// The short-lived access token is the thing that kept breaking: Dropbox issues them
    /// with a ~4 hour lifetime, so anything holding one in configuration stops working the
    /// same day. The refresh token does not expire, so this mints access tokens on demand
    /// and caches them — DROPBOX_ACCESS_TOKEN is deliberately not read at all.
    ///
    /// Registered as a singleton so the cache actually survives between calls.
    /// </summary>
    public class DropboxStorage : IDropboxStorage
    {
        private const string TokenEndpoint = "https://api.dropboxapi.com/oauth2/token";
        private const string DownloadEndpoint = "https://content.dropboxapi.com/2/files/download";
        private const string UploadEndpoint = "https://content.dropboxapi.com/2/files/upload";

        /// <summary>
        /// Refresh this far before the token actually expires, so a request that starts just
        /// under the wire doesn't land after it.
        /// </summary>
        private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly DropboxOptions _options;
        private readonly ILogger<DropboxStorage> _logger;

        // Guards the token refresh so N concurrent uploads perform one exchange, not N.
        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        private string? _cachedToken;
        private DateTimeOffset _cachedTokenExpiresAt = DateTimeOffset.MinValue;

        public DropboxStorage(
            IHttpClientFactory httpClientFactory,
            IOptions<DropboxOptions> options,
            ILogger<DropboxStorage> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_options.AppKey) &&
            !string.IsNullOrWhiteSpace(_options.AppSecret) &&
            !string.IsNullOrWhiteSpace(_options.RefreshToken);

        public string? ConfigurationHint
        {
            get
            {
                if (IsConfigured) return null;

                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(_options.AppKey)) missing.Add("DROPBOX_APP_KEY");
                if (string.IsNullOrWhiteSpace(_options.AppSecret)) missing.Add("DROPBOX_APP_SECRET");
                if (string.IsNullOrWhiteSpace(_options.RefreshToken)) missing.Add("DROPBOX_REFRESH_TOKEN");

                return $"Dropbox is disabled — missing {string.Join(", ", missing)}.";
            }
        }

        public async Task<DropboxResult> TryUploadTextAsync(
            string dropboxPath, string content, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("Skipping Dropbox upload of {Path}. {Hint}", dropboxPath, ConfigurationHint);
                return DropboxResult.NotConfigured(ConfigurationHint!);
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(content);

                var response = await SendWithTokenAsync(
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint)
                        {
                            Content = new ByteArrayContent(bytes)
                        };
                        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new
                        {
                            path = NormalizePath(dropboxPath),
                            mode = "overwrite",
                            mute = true
                        }));
                        return request;
                    },
                    cancellationToken);

                if (response is null)
                    return DropboxResult.Fail("Could not obtain a Dropbox access token.");

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Dropbox upload of {Path} failed: HTTP {Status} {Body}",
                            dropboxPath, (int)response.StatusCode, Truncate(body));
                        return DropboxResult.Fail($"HTTP {(int)response.StatusCode}");
                    }
                }

                _logger.LogInformation("Uploaded {Path} to Dropbox.", dropboxPath);
                return DropboxResult.Ok();
            }
            catch (Exception ex)
            {
                // Callers run during startup seeding — never let this escape.
                _logger.LogError(ex, "Dropbox upload of {Path} failed.", dropboxPath);
                return DropboxResult.Fail(ex.Message);
            }
        }

        public async Task<byte[]?> TryDownloadAsync(string dropboxPath, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("Skipping Dropbox download of {Path}. {Hint}", dropboxPath, ConfigurationHint);
                return null;
            }

            try
            {
                var response = await SendWithTokenAsync(
                    () =>
                    {
                        var request = new HttpRequestMessage(HttpMethod.Post, DownloadEndpoint);
                        request.Headers.Add("Dropbox-API-Arg", JsonSerializer.Serialize(new
                        {
                            path = NormalizePath(dropboxPath)
                        }));
                        return request;
                    },
                    cancellationToken);

                if (response is null) return null;

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogError("Dropbox download of {Path} failed: HTTP {Status} {Body}",
                            dropboxPath, (int)response.StatusCode, Truncate(body));
                        return null;
                    }

                    return await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dropbox download of {Path} failed.", dropboxPath);
                return null;
            }
        }

        public async Task<byte[]?> TryDownloadUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(nameof(DropboxStorage));
                var bytes = await client.GetByteArrayAsync(url, cancellationToken);

                // An expired or rotated share link returns Dropbox's HTML error page with a
                // 200, so the status code alone is not enough to tell success from failure.
                if (LooksLikeHtml(bytes))
                {
                    _logger.LogError(
                        "Share link {Url} returned HTML rather than a file — the link's st= signature has most " +
                        "likely expired or the file is no longer shared publicly.", url);
                    return null;
                }

                return bytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download share link {Url}.", url);
                return null;
            }
        }

        /// <summary>
        /// Issues a request with a valid bearer token, retrying once on 401 in case the
        /// cached token was revoked server-side before its stated expiry.
        /// </summary>
        private async Task<HttpResponseMessage?> SendWithTokenAsync(
            Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
        {
            var token = await GetAccessTokenAsync(forceRefresh: false, cancellationToken);
            if (token is null) return null;

            var client = _httpClientFactory.CreateClient(nameof(DropboxStorage));

            var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

            response.Dispose();
            _logger.LogInformation("Dropbox rejected the cached access token; refreshing and retrying once.");

            token = await GetAccessTokenAsync(forceRefresh: true, cancellationToken);
            if (token is null) return null;

            var retry = requestFactory();
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return await client.SendAsync(retry, cancellationToken);
        }

        /// <summary>Returns a cached token when it is still comfortably valid, otherwise mints one.</summary>
        private async Task<string?> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
        {
            if (!forceRefresh && _cachedToken is not null && DateTimeOffset.UtcNow < _cachedTokenExpiresAt)
                return _cachedToken;

            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                // Re-check inside the lock: another caller may have refreshed while we waited.
                if (!forceRefresh && _cachedToken is not null && DateTimeOffset.UtcNow < _cachedTokenExpiresAt)
                    return _cachedToken;

                var client = _httpClientFactory.CreateClient(nameof(DropboxStorage));

                using var response = await client.PostAsync(
                    TokenEndpoint,
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = _options.RefreshToken!,
                        ["client_id"] = _options.AppKey!,
                        ["client_secret"] = _options.AppSecret!
                    }),
                    cancellationToken);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // Most common cause: the refresh token was revoked, or the app's access
                    // was removed in the Dropbox console. Say so rather than "unauthorized".
                    _logger.LogError(
                        "Could not exchange the Dropbox refresh token: HTTP {Status} {Body}. " +
                        "If this persists, re-issue DROPBOX_REFRESH_TOKEN.",
                        (int)response.StatusCode, Truncate(body));

                    _cachedToken = null;
                    _cachedTokenExpiresAt = DateTimeOffset.MinValue;
                    return null;
                }

                using var doc = JsonDocument.Parse(body);
                var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;

                if (string.IsNullOrWhiteSpace(token))
                {
                    _logger.LogError("Dropbox token endpoint returned no access_token.");
                    return null;
                }

                // Dropbox reports expires_in in seconds (typically 14400 = 4 hours). Default
                // conservatively if it's absent rather than assuming a long life.
                var lifetime = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
                    ? TimeSpan.FromSeconds(seconds)
                    : TimeSpan.FromMinutes(30);

                _cachedToken = token;
                _cachedTokenExpiresAt = DateTimeOffset.UtcNow + lifetime - ExpiryMargin;

                _logger.LogInformation(
                    "Obtained a Dropbox access token, valid for {Minutes:F0} minutes.", lifetime.TotalMinutes);

                return token;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to obtain a Dropbox access token.");
                return null;
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        /// <summary>Dropbox paths must be absolute; a leading slash is easy to forget.</summary>
        private static string NormalizePath(string path) =>
            string.IsNullOrWhiteSpace(path) ? "/" : path.StartsWith('/') ? path : "/" + path;

        private static bool LooksLikeHtml(byte[] bytes)
        {
            if (bytes.Length == 0) return true;

            var head = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256)).TrimStart();
            return head.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase)
                || head.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
        }

        private static string Truncate(string value) =>
            value.Length <= 300 ? value : value[..300] + "…";
    }
}
