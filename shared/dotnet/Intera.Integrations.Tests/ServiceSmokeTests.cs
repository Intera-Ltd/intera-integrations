using Intera.Common.Authentication;
using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Infrastructure.Startup;
using Intera.Salesforce.Api.Clients;
using Intera.Salesforce.Api.Controllers;
using Intera.Salesforce.Api.Options;
using Intera.Salesforce.Api.Services;
using Intera.Xero.Api.Options;
using Intera.Xero.Api.Services;
using Intera.ZohoInventory.Api.Controllers;
using Intera.ZohoInventory.Api.Options;
using Intera.ZohoInventory.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;
using Xunit;

namespace Intera.Integrations.Tests;

public sealed class ServiceSmokeTests
{
    private static string RandomDataKeyBase64() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(ServiceSecretsOptions.ExpectedDataKeyBytes));

    [Fact]
    public async Task MissingServiceTokenOrDataKey_FailsStartupValidationBeforeListening()
    {
        var ct = TestContext.Current.CancellationToken;

        var missingTokenBuilder = WebApplication.CreateBuilder();
        missingTokenBuilder.WebHost.UseTestServer();
        missingTokenBuilder.AddInteraApi("MissingToken.Test");
        await using (var app = missingTokenBuilder.Build())
        {
            app.UseInteraApi(httpsRedirection: false);
            await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(ct));
        }

        var missingDataKeyBuilder = WebApplication.CreateBuilder();
        missingDataKeyBuilder.WebHost.UseTestServer();
        missingDataKeyBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ServiceSecretsOptions.ServiceTokenEnvironmentVariable] = "tok-1"
        });
        missingDataKeyBuilder.AddInteraApi("MissingDataKey.Test", requiresDataVolume: true);
        await using (var app = missingDataKeyBuilder.Build())
        {
            app.UseInteraApi(httpsRedirection: false);
            await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(ct));
        }
    }

    [Fact]
    public async Task SalesforcePipeline_EnforcesInboundServiceTokenAndExecutesEndToEndQueryAgainstMockUpstream()
    {
        var ct = TestContext.Current.CancellationToken;
        const string inboundSecret = "inbound-test-secret";

        var upstreamHandler = new StubHttpMessageHandler((request, _) =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.EndsWith("/services/oauth2/token", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"access_token":"sf-access-1","instance_url":"https://acme.my.salesforce.com"}"""));
            }

            if (url.Contains("SELECT%20COUNT()", StringComparison.Ordinal))
            {
                return Task.FromResult(StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"totalSize":2,"done":true,"records":[]}"""));
            }

            return Task.FromResult(StubHttpMessageHandler.Json(
                HttpStatusCode.OK,
                """{"totalSize":2,"done":true,"records":[{"Id":"001A","Name":"Acme Corp","Industry":"Technology"}]}"""));
        });

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [ServiceSecretsOptions.ServiceTokenEnvironmentVariable] = inboundSecret
        });
        builder.AddInteraApi("Intera.Salesforce.Api.Test");

        var mvc = builder.Services.AddControllers();
        mvc.PartManager.ApplicationParts.Clear();
        mvc.AddApplicationPart(typeof(AccountController).Assembly);

        builder.Services
            .AddOptions<SalesforceOptions>()
            .Configure(o =>
            {
                o.LoginUrl = "https://acme.my.salesforce.com";
                o.ClientId = "sf-client";
                o.ClientSecret = "sf-secret";
            })
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SalesforceOptions>, SalesforceOptionsValidator>();

        builder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>()
            .ConfigurePrimaryHttpMessageHandler(() => upstreamHandler);

        builder.Services.AddSingleton<ISalesforceOAuthService, SalesforceOAuthService>();
        builder.Services.AddScoped<ISalesforceClient, SalesforceClient>();
        builder.Services.AddScoped<ISObjectQueryService, SObjectQueryService>();

        await using var app = builder.Build();
        app.UseInteraApi(httpsRedirection: false);
        await app.StartAsync(ct);

        using var client = app.GetTestClient();

        var health = await client.GetAsync("/health", ct);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var unauthRefresh = await client.PostAsync("/oauth/refresh", content: null, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRefresh.StatusCode);

        var unauthQuery = await client.PostAsJsonAsync("/api/account/query", new { skip = 0, top = 10 }, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthQuery.StatusCode);

        using var authRefreshReq = new HttpRequestMessage(HttpMethod.Post, "/oauth/refresh");
        authRefreshReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inboundSecret);
        var authRefresh = await client.SendAsync(authRefreshReq, ct);
        Assert.Equal(HttpStatusCode.OK, authRefresh.StatusCode);

        using var queryReq = new HttpRequestMessage(HttpMethod.Post, "/api/account/query")
        {
            Content = JsonContent.Create(new { skip = 0, top = 10 })
        };
        queryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inboundSecret);

        var queryRes = await client.SendAsync(queryReq, ct);
        Assert.Equal(HttpStatusCode.OK, queryRes.StatusCode);
        Assert.Equal("2", queryRes.Headers.GetValues("X-Total-Count").Single());

        using var doc = JsonDocument.Parse(await queryRes.Content.ReadAsStringAsync(ct));
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        var rows = doc.RootElement.GetProperty("data");
        Assert.Equal(1, rows.GetArrayLength());
        Assert.Equal("Acme Corp", rows[0].GetProperty("account_name").GetString());
    }

    [Fact]
    public async Task ZohoAndXeroOAuthPipelines_EnforceServiceTokensOnEveryRouteAndEncryptStoredGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        const string secret = "inbound-secret-2";
        var tempDir = Path.Combine(Path.GetTempPath(), $"intera-smoke-{Guid.NewGuid():N}");

        try
        {
            var zohoBuilder = WebApplication.CreateBuilder();
            zohoBuilder.WebHost.UseTestServer();
            zohoBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServiceSecretsOptions.ServiceTokenEnvironmentVariable] = secret
            });
            zohoBuilder.AddInteraApi("Intera.ZohoInventory.Api.Test");
            var zohoMvc = zohoBuilder.Services.AddControllers();
            zohoMvc.PartManager.ApplicationParts.Clear();
            zohoMvc.AddApplicationPart(typeof(ZohoOAuthController).Assembly);

            zohoBuilder.Services
                .AddOptions<ZohoInventoryOptions>()
                .Configure(o =>
                {
                    o.OrganizationId = "org-1";
                    o.ClientId = "z-client";
                    o.ClientSecret = "z-secret";
                    o.RefreshToken = "z-refresh";
                })
                .ValidateOnStart();
            zohoBuilder.Services.AddSingleton<IValidateOptions<ZohoInventoryOptions>, ZohoInventoryOptionsValidator>();
            zohoBuilder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler((_, _) => Task.FromResult(
                    StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"access_token":"z-acc","expires_in":3600}"""))));
            zohoBuilder.Services.AddSingleton<IZohoOAuthService, ZohoOAuthService>();

            await using (var zohoApp = zohoBuilder.Build())
            {
                zohoApp.UseInteraApi(httpsRedirection: false);
                await zohoApp.StartAsync(ct);
                using var zohoClient = zohoApp.GetTestClient();

                var unauth = await zohoClient.PostAsync("/oauth/refresh", content: null, ct);
                Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

                using var authReq = new HttpRequestMessage(HttpMethod.Post, "/oauth/refresh");
                authReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                var auth = await zohoClient.SendAsync(authReq, ct);
                Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
            }

            var xeroTokenPath = Path.Combine(tempDir, "xero-token.json");
            var xeroBuilder = WebApplication.CreateBuilder();
            xeroBuilder.WebHost.UseTestServer();
            xeroBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServiceSecretsOptions.ServiceTokenEnvironmentVariable] = secret,
                [ServiceSecretsOptions.DataKeyEnvironmentVariable] = RandomDataKeyBase64()
            });
            xeroBuilder.AddInteraApi("Intera.Xero.Api.Test", requiresDataVolume: true);
            var xeroMvc = xeroBuilder.Services.AddControllers();
            xeroMvc.PartManager.ApplicationParts.Clear();
            xeroMvc.AddApplicationPart(typeof(Intera.Xero.Api.Controllers.OAuthController).Assembly);

            xeroBuilder.Services
                .AddOptions<XeroOptions>()
                .Configure(o =>
                {
                    o.ClientId = "x-client";
                    o.ClientSecret = "x-secret";
                    o.OAuthTokenFilePath = xeroTokenPath;
                    o.RedirectUriFilePath = Path.Combine(tempDir, "xero-redirect.json");
                })
                .ValidateOnStart();
            xeroBuilder.Services.AddSingleton<IValidateOptions<XeroOptions>, XeroOptionsValidator>();
            xeroBuilder.Services.AddHttpClient(XeroTokenService.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler((_, _) => Task.FromResult(
                    StubHttpMessageHandler.Json(HttpStatusCode.OK, """{"access_token":"x-acc-secret","refresh_token":"x-ref-secret","expires_in":1800}"""))));
            xeroBuilder.Services.AddInteraFileOAuthStores(
                sp => sp.GetRequiredService<IOptionsMonitor<XeroOptions>>().CurrentValue.OAuthTokenFilePath,
                sp => sp.GetRequiredService<IOptionsMonitor<XeroOptions>>().CurrentValue.RedirectUriFilePath);
            xeroBuilder.Services.AddSingleton<XeroTokenService>();

            await using var xeroApp = xeroBuilder.Build();
            xeroApp.UseInteraApi(httpsRedirection: false);
            await xeroApp.StartAsync(ct);
            using var xeroClient = xeroApp.GetTestClient();

            // Even /oauth/status and /oauth/callback must reject unauthenticated callers.
            Assert.Equal(HttpStatusCode.Unauthorized, (await xeroClient.GetAsync("/oauth/status", ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await xeroClient.GetAsync("/oauth/callback?code=a&state=b", ct)).StatusCode);

            using var putReq = new HttpRequestMessage(HttpMethod.Put, "/oauth/redirect-uri")
            {
                Content = JsonContent.Create(new { redirect_uri = "https://core.example.com/api/webhooks/xero" })
            };
            putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            var putRes = await xeroClient.SendAsync(putReq, ct);
            Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

            using var authUrlReq = new HttpRequestMessage(HttpMethod.Get, "/oauth/authorize-url");
            authUrlReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            var authUrlRes = await xeroClient.SendAsync(authUrlReq, ct);
            Assert.Equal(HttpStatusCode.OK, authUrlRes.StatusCode);

            using var authUrlDoc = JsonDocument.Parse(await authUrlRes.Content.ReadAsStringAsync(ct));
            var authorizeUrl = authUrlDoc.RootElement.GetProperty("data").GetProperty("url").GetString()!;
            var state = HttpUtility.ParseQueryString(new Uri(authorizeUrl).Query)["state"]!;

            using var callbackReq = new HttpRequestMessage(HttpMethod.Get, $"/oauth/callback?code=code-123&state={Uri.EscapeDataString(state)}");
            callbackReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            var callbackRes = await xeroClient.SendAsync(callbackReq, ct);
            Assert.Equal(HttpStatusCode.OK, callbackRes.StatusCode);

            using var statusReq = new HttpRequestMessage(HttpMethod.Get, "/oauth/status");
            statusReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            var statusRes = await xeroClient.SendAsync(statusReq, ct);
            Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);
            using var statusDoc = JsonDocument.Parse(await statusRes.Content.ReadAsStringAsync(ct));
            Assert.True(statusDoc.RootElement.GetProperty("has_access_token").GetBoolean());
            Assert.True(statusDoc.RootElement.GetProperty("has_refresh_token").GetBoolean());
            Assert.Equal("intera", statusDoc.RootElement.GetProperty("redirect_uri_source").GetString());

            // Verify persisted token file is encrypted at rest.
            var persistedText = await File.ReadAllTextAsync(xeroTokenPath, ct);
            Assert.DoesNotContain("x-acc-secret", persistedText, StringComparison.Ordinal);
            Assert.DoesNotContain("x-ref-secret", persistedText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
