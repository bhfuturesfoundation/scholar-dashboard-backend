using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Auth.Services.Interfaces.Notifications;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PushServiceSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace Auth.Services.Services.Notifications
{
    /// <summary>
    /// Web push over VAPID.
    ///
    /// VAPID is how a push service (FCM, Mozilla's autopush, Apple's) knows the message
    /// really came from this application: the server signs each request with a private key
    /// whose public half the browser was given at subscribe time. Both halves are
    /// configuration, not secrets to generate per request — see
    /// <see cref="GenerateKeyPair"/> for producing them once.
    ///
    /// Every failure path here is non-throwing. A push send happens inside a loop over a
    /// scholar's devices, inside a loop over an audience; one unreachable phone must cost
    /// one log line, not a broadcast.
    /// </summary>
    public class WebPushSender : IPushSender
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly PushServiceClient _client;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebPushSender> _logger;

        public WebPushSender(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<WebPushSender> logger)
        {
            _configuration = configuration;
            _logger = logger;

            _client = new PushServiceClient(httpClient);

            if (IsConfigured)
            {
                _client.DefaultAuthentication = new VapidAuthentication(PublicKey!, PrivateKey!)
                {
                    // The push service uses this to contact whoever operates the app if it
                    // starts misbehaving. A mailto: or https: URL; anything else is rejected.
                    Subject = Subject
                };
            }
        }

        private string? PrivateKey => Trimmed("VAPID_PRIVATE_KEY");

        public string? PublicKey => Trimmed("VAPID_PUBLIC_KEY");

        private string Subject
        {
            get
            {
                var configured = Trimmed("VAPID_SUBJECT");
                if (!string.IsNullOrWhiteSpace(configured)) return configured;
                return "mailto:info@bhfuturesfoundation.org";
            }
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);

        public string ConfigurationHint =>
            IsConfigured
                ? "Web push is configured."
                : "Set VAPID_PUBLIC_KEY and VAPID_PRIVATE_KEY to enable web push. " +
                  "Generate a pair once with WebPushSender.GenerateKeyPair() and keep the private key secret.";

        private string? Trimmed(string key)
        {
            var value = _configuration[key]?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public async Task<PushSendResult> SendAsync(
            PushTarget target, PushPayload payload, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured) return PushSendResult.Failed("Web push is not configured.");

            if (string.IsNullOrWhiteSpace(target.Endpoint)
                || string.IsNullOrWhiteSpace(target.P256dh)
                || string.IsNullOrWhiteSpace(target.Auth))
            {
                return PushSendResult.Failed("Subscription is missing an endpoint or a key.");
            }

            var subscription = new PushServiceSubscription { Endpoint = target.Endpoint };
            subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
            subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);

            var message = new PushMessage(JsonSerializer.Serialize(payload, JsonOptions))
            {
                // Same tag replaces rather than stacks on the device, so a reminder that is
                // retried does not produce two entries in the notification shade.
                Topic = payload.Tag,

                // A deadline reminder is still worth showing an hour late; four hours is
                // long enough to survive a phone being off, short enough that nothing
                // arrives so stale it confuses.
                TimeToLive = 4 * 60 * 60
            };

            try
            {
                await _client.RequestPushMessageDeliveryAsync(subscription, message, cancellationToken);
                return PushSendResult.Ok();
            }
            catch (PushServiceClientException ex)
                when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // The browser cleared its storage, or the PWA was uninstalled. This
                // subscription will never work again.
                return PushSendResult.Expired($"Subscription is gone ({(int)ex.StatusCode}).");
            }
            catch (PushServiceClientException ex)
            {
                _logger.LogWarning(
                    "Push delivery to {Host} failed with {Status}.",
                    SafeHost(target.Endpoint), ex.StatusCode);

                return PushSendResult.Failed($"Push service returned {(int)ex.StatusCode}.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push delivery to {Host} threw.", SafeHost(target.Endpoint));
                return PushSendResult.Failed(ex.Message);
            }
        }

        /// <summary>
        /// Host only — a push endpoint's path is a bearer capability for that device, so it
        /// must not reach the logs.
        /// </summary>
        private static string SafeHost(string endpoint) =>
            Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : "unknown";

        /// <summary>
        /// Generates a VAPID key pair.
        ///
        /// A VAPID key pair is just a P-256 keypair in the encoding the Web Push spec asks
        /// for: the public key is the uncompressed EC point (0x04 ‖ X ‖ Y) and the private
        /// key is the scalar D, both base64url with no padding. Written out here rather than
        /// taken from a library helper so the format is visible and the operations console
        /// can offer a one-click generate without a second dependency.
        ///
        /// Run once. Rotating the pair invalidates every existing subscription, because the
        /// browser bound its subscription to the public key it was given.
        /// </summary>
        public static (string PublicKey, string PrivateKey) GenerateKeyPair()
        {
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var parameters = ecdh.ExportParameters(includePrivateParameters: true);

            var x = Pad(parameters.Q.X!, 32);
            var y = Pad(parameters.Q.Y!, 32);
            var d = Pad(parameters.D!, 32);

            var publicKey = new byte[65];
            publicKey[0] = 0x04;
            x.CopyTo(publicKey, 1);
            y.CopyTo(publicKey, 33);

            return (Base64Url(publicKey), Base64Url(d));
        }

        /// <summary>
        /// Left-pads to the curve's field size. .NET strips leading zero bytes, so roughly
        /// one key in 256 exports a 31-byte coordinate — which the push service rejects as
        /// malformed, intermittently and only in production.
        /// </summary>
        private static byte[] Pad(byte[] value, int length)
        {
            if (value.Length == length) return value;
            if (value.Length > length) return value[^length..];

            var padded = new byte[length];
            value.CopyTo(padded, length - value.Length);
            return padded;
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
