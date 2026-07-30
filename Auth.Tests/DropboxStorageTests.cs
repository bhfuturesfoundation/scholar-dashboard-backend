using Auth.Services.Interfaces.Storage;
using Auth.Services.Services.Storage;
using Auth.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth.Tests;

/// <summary>
/// Tests for Dropbox credential handling.
///
/// The bug these guard against: Dropbox access tokens expire after about four hours, so
/// anything holding one stops working the same day. Worse, the previous static helper threw
/// when its environment variables were missing, and it was called from the startup seeders
/// — which run unguarded before app.Run(). A missing or stale Dropbox credential therefore
/// crash-looped the whole API over an optional password-export CSV.
///
/// The contract being pinned: unconfigured or failing Dropbox degrades to a skipped upload,
/// never an exception.
/// </summary>
public class DropboxStorageTests
{
    private static DropboxStorage Build(DropboxOptions options) =>
        new(new StubHttpClientFactory(), Options.Create(options), NullLogger<DropboxStorage>.Instance);

    private static DropboxOptions FullyConfigured() => new()
    {
        AppKey = "app-key",
        AppSecret = "app-secret",
        RefreshToken = "refresh-token"
    };

    [Fact]
    public void FullyConfigured_ReportsConfigured()
    {
        var storage = Build(FullyConfigured());

        Assert.True(storage.IsConfigured);
        Assert.Null(storage.ConfigurationHint);
    }

    [Fact]
    public void NoCredentials_ReportsNotConfigured_WithoutThrowing()
    {
        var storage = Build(new DropboxOptions());

        Assert.False(storage.IsConfigured);
        Assert.NotNull(storage.ConfigurationHint);
    }

    [Theory]
    [InlineData(null, "secret", "refresh", "DROPBOX_APP_KEY")]
    [InlineData("key", null, "refresh", "DROPBOX_APP_SECRET")]
    [InlineData("key", "secret", null, "DROPBOX_REFRESH_TOKEN")]
    public void MissingVariable_IsNamedInTheHint(string? key, string? secret, string? refresh, string expected)
    {
        // The hint goes into the startup log. "Dropbox failed" sends someone reading code;
        // naming the variable ends the investigation immediately.
        var storage = Build(new DropboxOptions { AppKey = key, AppSecret = secret, RefreshToken = refresh });

        Assert.False(storage.IsConfigured);
        Assert.Contains(expected, storage.ConfigurationHint);
    }

    [Fact]
    public async Task Upload_WhenNotConfigured_IsSkippedNotThrown()
    {
        // The headline regression. This call sits inside SeedUsersAsync, which runs before
        // app.Run(); if it throws, the container never starts.
        var storage = Build(new DropboxOptions());

        var result = await storage.TryUploadTextAsync("/passwords.csv", "a,b,c");

        Assert.False(result.Success);
        Assert.True(result.Skipped);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Download_WhenNotConfigured_ReturnsNullNotThrown()
    {
        var storage = Build(new DropboxOptions());

        var bytes = await storage.TryDownloadAsync("/users.csv");

        Assert.Null(bytes);
    }

    [Fact]
    public async Task ShareLinkReturningHtml_IsTreatedAsFailure()
    {
        // An expired st= signature makes Dropbox serve its HTML error page with HTTP 200.
        // Status alone can't distinguish that from a real CSV, and feeding the HTML to
        // CsvHelper produced a confusing parse error far from the actual cause.
        var factory = new StubHttpClientFactory(
            "<!DOCTYPE html><html><head><title>Dropbox - Error</title></head></html>");

        var storage = new DropboxStorage(
            factory, Options.Create(FullyConfigured()), NullLogger<DropboxStorage>.Instance);

        var bytes = await storage.TryDownloadUrlAsync("https://www.dropbox.com/scl/fi/x/users.csv?dl=1");

        Assert.Null(bytes);
    }

    [Fact]
    public async Task ShareLinkReturningCsv_IsReturned()
    {
        var factory = new StubHttpClientFactory("Email,Password\na@b.ba,secret\n");

        var storage = new DropboxStorage(
            factory, Options.Create(FullyConfigured()), NullLogger<DropboxStorage>.Instance);

        var bytes = await storage.TryDownloadUrlAsync("https://www.dropbox.com/scl/fi/x/users.csv?dl=1");

        Assert.NotNull(bytes);
        Assert.Contains("a@b.ba", System.Text.Encoding.UTF8.GetString(bytes!));
    }

    [Fact]
    public async Task NetworkFailure_IsSwallowed()
    {
        var storage = new DropboxStorage(
            new StubHttpClientFactory(throwOnSend: true),
            Options.Create(FullyConfigured()),
            NullLogger<DropboxStorage>.Instance);

        // Whatever goes wrong at the transport layer, startup must survive it.
        var bytes = await storage.TryDownloadUrlAsync("https://www.dropbox.com/x");
        var upload = await storage.TryUploadTextAsync("/x.csv", "data");

        Assert.Null(bytes);
        Assert.False(upload.Success);
    }

    [Fact]
    public void OptionsHaveNoAccessTokenProperty()
    {
        // A long-lived access token is exactly what kept expiring. Pin its absence so nobody
        // reintroduces DROPBOX_ACCESS_TOKEN as a "simpler" option.
        var property = typeof(DropboxOptions).GetProperty("AccessToken");

        Assert.Null(property);
    }

    /// <summary>Minimal IHttpClientFactory returning a canned body, or throwing.</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly string _body;
        private readonly bool _throwOnSend;

        public StubHttpClientFactory(string body = "", bool throwOnSend = false)
        {
            _body = body;
            _throwOnSend = throwOnSend;
        }

        public HttpClient CreateClient(string name) => new(new StubHandler(_body, _throwOnSend));

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly string _body;
            private readonly bool _throwOnSend;

            public StubHandler(string body, bool throwOnSend)
            {
                _body = body;
                _throwOnSend = throwOnSend;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (_throwOnSend) throw new HttpRequestException("simulated network failure");

                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(_body)
                });
            }
        }
    }
}
