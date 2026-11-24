using Intera.Common.Authentication;
using Intera.Common.Infrastructure.OAuth;
using Intera.Eurobank.Api.Infrastructure;
using Intera.Eurobank.Api.Options;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Options;
using Intera.Jira.Api.Services;
using Intera.Revolut.Api.Infrastructure;
using Intera.Revolut.Api.Options;
using Intera.Revolut.Api.Services;
using Intera.Xero.Api.Infrastructure;
using Intera.Xero.Api.Options;
using Intera.Xero.Api.Services;
using Intera.Zendesk.Api.Infrastructure;
using Intera.Zendesk.Api.Options;
using Intera.Zendesk.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Web;
using Xunit;

namespace Intera.Integrations.Tests;

public sealed class OAuthTokenServicesTests
{
    private static ServiceDataKey NewDataKey() =>
        new(RandomNumberGenerator.GetBytes(ServiceSecretsOptions.ExpectedDataKeyBytes));

    [Fact]
    public async Task FileOAuthTokenStore_EncryptsWithAes256GcmReadsLegacyPlaintextAndRejectsTampering()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = Path.Combine(Path.GetTempPath(), $"intera-token-store-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);
            var tokenPath = Path.Combine(tempDir, "oauth-token.json");
            var key1 = NewDataKey();
            var key2 = NewDataKey();

            // 1. Legacy plaintext JSON on an upgraded volume is read transparently, then encrypted on next save.
            var legacyPlaintext = JsonSerializer.Serialize(new OAuthToken(
                "Xero",
                "legacy-access-secret",
                "legacy-refresh-secret",
                "Bearer",
                "accounting",
                DateTimeOffset.UtcNow.AddMinutes(20),
                DateTimeOffset.UtcNow));
            await File.WriteAllTextAsync(tokenPath, legacyPlaintext, ct);

            var store1 = new FileOAuthTokenStore(() => tokenPath, key1);
            var migratedRead = await store1.GetAsync("Xero", ct);
            Assert.NotNull(migratedRead);
            Assert.Equal("legacy-access-secret", migratedRead.AccessToken);
            Assert.Equal("legacy-refresh-secret", migratedRead.RefreshToken);

            // 2. Saving writes AES-256-GCM ciphertext; raw file never contains plaintext secrets.
            await store1.SaveAsync(
                new OAuthToken("Xero", "rotated-access-secret", "rotated-refresh-secret", "Bearer", "accounting", DateTimeOffset.UtcNow.AddMinutes(30), DateTimeOffset.UtcNow),
                ct);

            var rawDiskText = await File.ReadAllTextAsync(tokenPath, ct);
            Assert.DoesNotContain("rotated-access-secret", rawDiskText, StringComparison.Ordinal);
            Assert.DoesNotContain("rotated-refresh-secret", rawDiskText, StringComparison.Ordinal);
            Assert.Contains("\"alg\": \"A256GCM\"", rawDiskText, StringComparison.Ordinal);

            var decrypted = await store1.GetAsync("Xero", ct);
            Assert.NotNull(decrypted);
            Assert.Equal("rotated-access-secret", decrypted.AccessToken);
            Assert.Equal("rotated-refresh-secret", decrypted.RefreshToken);

            // 3. Wrong data key fails authentication tag verification.
            var storeWrongKey = new FileOAuthTokenStore(() => tokenPath, key2);
            await Assert.ThrowsAnyAsync<CryptographicException>(() => storeWrongKey.GetAsync("Xero", ct));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task XeroTokenService_EnforcesSingleUseStatePinsRedirectUriAndDeduplicatesConcurrentRefreshes()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = Path.Combine(Path.GetTempPath(), $"intera-xero-{Guid.NewGuid():N}");

        try
        {
            var options = new XeroOptions
            {
                ClientId = "xero-client",
                ClientSecret = "xero-secret",
                AuthorizeUrl = "https://login.xero.com/identity/connect/authorize",
                TokenUrl = "https://identity.xero.com/connect/token",
                OAuthTokenFilePath = Path.Combine(tempDir, "token.json"),
                RedirectUriFilePath = Path.Combine(tempDir, "redirect.json")
            };

            var refreshCalls = 0;
            var handler = new StubHttpMessageHandler(async (req, token) =>
            {
                var form = await req.Content!.ReadAsStringAsync(token).ConfigureAwait(false);
                if (form.Contains("grant_type=authorization_code", StringComparison.Ordinal))
                {
                    return StubHttpMessageHandler.Json(
                        HttpStatusCode.OK,
                        """{"access_token":"acc-initial","refresh_token":"ref-1","expires_in":1800,"token_type":"Bearer","scope":"accounting.transactions.read"}""");
                }

                var callNum = Interlocked.Increment(ref refreshCalls);
                await Task.Delay(50, token).ConfigureAwait(false);
                return StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    $$$"""{"access_token":"acc-refreshed-{{{callNum}}}","refresh_token":"ref-2","expires_in":1800,"token_type":"Bearer"}""");
            });

            var monitor = new StaticOptionsMonitor<XeroOptions>(options);
            var tokenStore = new FileOAuthTokenStore(() => monitor.CurrentValue.OAuthTokenFilePath, NewDataKey());
            var redirectStore = new FileOAuthRedirectUriStore(() => monitor.CurrentValue.RedirectUriFilePath);
            var factory = new StubHttpClientFactory(() => new HttpClient(handler, disposeHandler: false));
            var service = new XeroTokenService(monitor, tokenStore, redirectStore, factory, NullLogger<XeroTokenService>.Instance);

            const string firstWebhook = "https://core.example.com/api/webhooks/xero-first";
            const string secondWebhook = "https://core.example.com/api/webhooks/xero-second";
            await service.RegisterRedirectUriAsync(firstWebhook, ct);

            var authUrl = await service.BuildAuthorizationUrlAsync(ct);
            var state = ExtractQueryParameter(authUrl, "state");
            Assert.Equal(firstWebhook, ExtractQueryParameter(authUrl, "redirect_uri"));

            await service.RegisterRedirectUriAsync(secondWebhook, ct);

            await Assert.ThrowsAsync<XeroRequestException>(
                () => service.ExchangeAuthorizationCodeAsync("code-1", "unknown-state", ct));

            await service.ExchangeAuthorizationCodeAsync("code-1", state, ct);
            Assert.Contains($"redirect_uri={Uri.EscapeDataString(firstWebhook)}", handler.RequestBodies[0]!, StringComparison.Ordinal);

            await Assert.ThrowsAsync<XeroRequestException>(
                () => service.ExchangeAuthorizationCodeAsync("code-1", state, ct));

            var refreshedTokens = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => service.RefreshAfterRejectionAsync("acc-initial", ct)));

            Assert.Equal(1, refreshCalls);
            Assert.All(refreshedTokens, t => Assert.Equal("acc-refreshed-1", t));

            options.RedirectUri = "https://custom.example.com/oauth/callback";
            var conflict = await Assert.ThrowsAsync<OAuthRedirectUriConflictException>(
                () => service.RegisterRedirectUriAsync(secondWebhook, ct));

            var controller = new Intera.Xero.Api.Controllers.OAuthController(service)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            var mapped = Intera.Xero.Api.Controllers.XeroControllerBase.Map(controller, conflict);
            Assert.NotNull(mapped);
            Assert.Equal(StatusCodes.Status409Conflict, mapped.StatusCode);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task JiraTokenService_DeduplicatesConcurrentExpiredTokenRefreshesAndEnforcesState()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = Path.Combine(Path.GetTempPath(), $"intera-jira-{Guid.NewGuid():N}");

        try
        {
            var options = new JiraOptions
            {
                ClientId = "jira-client",
                ClientSecret = "jira-secret",
                OAuthTokenFilePath = Path.Combine(tempDir, "token.json"),
                RedirectUriFilePath = Path.Combine(tempDir, "redirect.json")
            };

            var refreshCalls = 0;
            var handler = new StubHttpMessageHandler(async (_, token) =>
            {
                Interlocked.Increment(ref refreshCalls);
                await Task.Delay(50, token).ConfigureAwait(false);
                return StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"access_token":"jira-fresh","refresh_token":"jira-ref-2","expires_in":3600,"token_type":"Bearer"}""");
            });

            var monitor = new StaticOptionsMonitor<JiraOptions>(options);
            var tokenStore = new FileOAuthTokenStore(() => monitor.CurrentValue.OAuthTokenFilePath, NewDataKey());
            var redirectStore = new FileOAuthRedirectUriStore(() => monitor.CurrentValue.RedirectUriFilePath);

            await tokenStore.SaveAsync(
                new OAuthToken("Jira", "jira-expired", "jira-ref-1", "Bearer", "read:jira-work", DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow),
                ct);

            var factory = new StubHttpClientFactory(() => new HttpClient(handler, disposeHandler: false));
            var service = new JiraTokenService(monitor, tokenStore, redirectStore, factory, NullLogger<JiraTokenService>.Instance);

            var tokens = await Task.WhenAll(
                Enumerable.Range(0, 6).Select(_ => service.GetAccessTokenAsync(ct)));

            Assert.Equal(1, refreshCalls);
            Assert.All(tokens, t => Assert.Equal("jira-fresh", t));

            await Assert.ThrowsAsync<JiraRequestException>(
                () => service.ExchangeAuthorizationCodeAsync("code", "forged-state", ct));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ZendeskTokenService_EnforcesSingleUseStateAndConflictDetection()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = Path.Combine(Path.GetTempPath(), $"intera-zd-{Guid.NewGuid():N}");

        try
        {
            var options = new ZendeskOptions
            {
                Subdomain = "acme",
                ClientId = "zd-client",
                ClientSecret = "zd-secret",
                OAuthTokenFilePath = Path.Combine(tempDir, "token.json"),
                RedirectUriFilePath = Path.Combine(tempDir, "redirect.json")
            };

            var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"access_token":"zd-acc","refresh_token":"zd-ref","expires_in":7200,"token_type":"bearer","scope":"read"}""")));

            var monitor = new StaticOptionsMonitor<ZendeskOptions>(options);
            var tokenStore = new FileOAuthTokenStore(() => monitor.CurrentValue.OAuthTokenFilePath, NewDataKey());
            var redirectStore = new FileOAuthRedirectUriStore(() => monitor.CurrentValue.RedirectUriFilePath);
            var factory = new StubHttpClientFactory(() => new HttpClient(handler, disposeHandler: false));
            var service = new ZendeskTokenService(monitor, tokenStore, redirectStore, factory, NullLogger<ZendeskTokenService>.Instance);

            await service.RegisterRedirectUriAsync("https://core.example.com/api/webhooks/zd", ct);
            var url = await service.BuildAuthorizationUrlAsync(ct);
            var state = ExtractQueryParameter(url, "state");

            await service.ExchangeAuthorizationCodeAsync("zd-code", state, ct);
            await Assert.ThrowsAsync<ZendeskRequestException>(
                () => service.ExchangeAuthorizationCodeAsync("zd-code", state, ct));

            options.RedirectUri = "https://override.example.com/oauth/callback";
            await Assert.ThrowsAsync<OAuthRedirectUriConflictException>(
                () => service.RegisterRedirectUriAsync("https://core.example.com/api/webhooks/zd", ct));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RevolutOAuthService_SignsJwtAssertionFromInlinePemAndEnforcesSingleUseState()
    {
        var ct = TestContext.Current.CancellationToken;
        var tempDir = Path.Combine(Path.GetTempPath(), $"intera-rev-{Guid.NewGuid():N}");

        try
        {
            using var rsa = RSA.Create(2048);
            var inlinePem = rsa.ExportPkcs8PrivateKeyPem().Replace("\n", "\\n", StringComparison.Ordinal);

            var options = new RevolutOptions
            {
                ClientId = "rev-client",
                ClientAssertionIssuer = "core.example.com",
                PrivateKeyPem = inlinePem,
                OAuthTokenFilePath = Path.Combine(tempDir, "token.json"),
                RedirectUriFilePath = Path.Combine(tempDir, "redirect.json")
            };

            var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"access_token":"rev-acc","refresh_token":"rev-ref","expires_in":2400,"token_type":"bearer"}""")));

            var monitor = new StaticOptionsMonitor<RevolutOptions>(options);
            var tokenStore = new FileOAuthTokenStore(() => monitor.CurrentValue.OAuthTokenFilePath, NewDataKey());
            var redirectStore = new FileOAuthRedirectUriStore(() => monitor.CurrentValue.RedirectUriFilePath);
            var factory = new StubHttpClientFactory(() => new HttpClient(handler, disposeHandler: false));
            var service = new RevolutOAuthService(
                monitor,
                factory,
                new RsaJwtClientAssertionFactory(),
                tokenStore,
                redirectStore,
                NullLogger<RevolutOAuthService>.Instance);

            await service.RegisterRedirectUriAsync("https://core.example.com/api/webhooks/revolut", ct);
            var dto = await service.BuildAuthorizeUrlAsync(ct);
            var state = ExtractQueryParameter(dto.Url, "state");

            await service.ExchangeAuthorizationCodeAsync("rev-code", state, ct);
            Assert.Contains("client_assertion=", handler.RequestBodies[0]!, StringComparison.Ordinal);

            await Assert.ThrowsAsync<RevolutStateException>(
                () => service.ExchangeAuthorizationCodeAsync("rev-code", state, ct));
            Assert.Equal("rev-acc", await service.GetAccessTokenAsync(ct));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void EurobankClientCertificateHandler_LoadsEphemeralPemPairAndCompactPkcs12()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=EurobankTestClient", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var certPem = cert.ExportCertificatePem();
        var keyPem = rsa.ExportPkcs8PrivateKeyPem();

        using var pemHandler = new EurobankClientCertificateHandler(new StaticOptionsMonitor<EurobankOptions>(new EurobankOptions
        {
            ClientCertificatePem = certPem,
            ClientPrivateKeyPem = keyPem
        }));
        Assert.Single(pemHandler.ClientCertificates);

        var pfxBase64 = Convert.ToBase64String(cert.Export(X509ContentType.Pkcs12, "test-pass"));
        using var pfxHandler = new EurobankClientCertificateHandler(new StaticOptionsMonitor<EurobankOptions>(new EurobankOptions
        {
            ClientCertificatePfxBase64 = pfxBase64,
            ClientCertificatePassword = "test-pass"
        }));
        Assert.Single(pfxHandler.ClientCertificates);
    }

    private static string ExtractQueryParameter(string url, string parameter)
    {
        var uri = new Uri(url);
        var parsed = HttpUtility.ParseQueryString(uri.Query);
        return parsed[parameter] ?? throw new InvalidOperationException($"Missing query parameter '{parameter}' in '{url}'.");
    }
}
