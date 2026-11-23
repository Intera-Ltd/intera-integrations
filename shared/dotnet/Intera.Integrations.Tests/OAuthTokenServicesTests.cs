using Intera.Common.Infrastructure.OAuth;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Options;
using Intera.Jira.Api.Services;
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
using System.Web;
using Xunit;

namespace Intera.Integrations.Tests;

public sealed class OAuthTokenServicesTests
{
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
            var tokenStore = new FileOAuthTokenStore(() => monitor.CurrentValue.OAuthTokenFilePath);
            var redirectStore = new FileOAuthRedirectUriStore(() => monitor.CurrentValue.RedirectUriFilePath);
            var factory = new StubHttpClientFactory(() => new HttpClient(handler, disposeHandler: false));
            var service = new XeroTokenService(monitor, tokenStore, redirectStore, factory, NullLogger<XeroTokenService>.Instance);

            const string firstWebhook = "https://core.example.com/api/webhooks/xero-first";
            const string secondWebhook = "https://core.example.com/api/webhooks/xero-second";
            await service.RegisterRedirectUriAsync(firstWebhook, ct);

            var authUrl = await service.BuildAuthorizationUrlAsync(ct);
            var state = ExtractQueryParameter(authUrl, "state");
            Assert.Equal(firstWebhook, ExtractQueryParameter(authUrl, "redirect_uri"));

            // Registering a newer redirect URI before callback must NOT break in-flight state exchange.
            await service.RegisterRedirectUriAsync(secondWebhook, ct);

            await Assert.ThrowsAsync<XeroRequestException>(
                () => service.ExchangeAuthorizationCodeAsync("code-1", "unknown-state", ct));

            await service.ExchangeAuthorizationCodeAsync("code-1", state, ct);
            Assert.Contains($"redirect_uri={Uri.EscapeDataString(firstWebhook)}", handler.RequestBodies[0]!, StringComparison.Ordinal);

            // Reusing the same single-use state must be rejected.
            await Assert.ThrowsAsync<XeroRequestException>(
                () => service.ExchangeAuthorizationCodeAsync("code-1", state, ct));

            // 8 concurrent rejections of "acc-initial" must coalesce into a single upstream refresh request.
            var refreshedTokens = await Task.WhenAll(
                Enumerable.Range(0, 8).Select(_ => service.RefreshAfterRejectionAsync("acc-initial", ct)));

            Assert.Equal(1, refreshCalls);
            Assert.All(refreshedTokens, t => Assert.Equal("acc-refreshed-1", t));

            // Conflict when explicit Xero:RedirectUri is set to a different URL -> 409 on controller mapping.
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
            var tokenStore = new FileOAuthTokenStore(() => monitor.CurrentValue.OAuthTokenFilePath);
            var redirectStore = new FileOAuthRedirectUriStore(() => monitor.CurrentValue.RedirectUriFilePath);

            // Seed an already-expired token on disk.
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
            var tokenStore = new FileOAuthTokenStore(() => monitor.CurrentValue.OAuthTokenFilePath);
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

    private static string ExtractQueryParameter(string url, string parameter)
    {
        var uri = new Uri(url);
        var parsed = HttpUtility.ParseQueryString(uri.Query);
        return parsed[parameter] ?? throw new InvalidOperationException($"Missing query parameter '{parameter}' in '{url}'.");
    }
}
