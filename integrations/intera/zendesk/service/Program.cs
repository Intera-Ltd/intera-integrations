using Intera.Common.Authentication;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Infrastructure.Startup;
using Intera.Zendesk.Api.Clients;
using Intera.Zendesk.Api.Infrastructure;
using Intera.Zendesk.Api.Options;
using Intera.Zendesk.Api.Services;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Zendesk.Api");

builder.Services.Configure<ZendeskOptions>(builder.Configuration.GetSection(ZendeskOptions.SectionName));

// One named client; the Singleton client below takes it from the factory per call, so no handler is pinned for the
// process lifetime.
builder.Services.AddHttpClient(ZendeskClient.HttpClientName, (serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ZendeskOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

// The grant rotates its refresh token on every use, so it lives in this service's own file — see ZendeskTokenService.
builder.Services.AddSingleton<IOAuthTokenStore>(serviceProvider => new FileOAuthTokenStore(
    () => serviceProvider.GetRequiredService<IOptionsMonitor<ZendeskOptions>>().CurrentValue.OAuthTokenFilePath));

// The redirect URL INTERA registers (its webhook address) is kept next to the grant, for the same reason.
builder.Services.AddSingleton<IZendeskRedirectUriStore, FileZendeskRedirectUriStore>();
builder.Services.AddSingleton<ZendeskTokenService>();
builder.Services.AddSingleton<ZendeskResponseCache>();
builder.Services.AddSingleton<ZendeskClient>();
builder.Services.AddScoped<IZendeskDataService, ZendeskDataService>();
builder.Services.AddHostedService<ZendeskTokenKeepAliveService>();

builder.Services.AddSingleton<IAccessTokenStore>(serviceProvider => new StaticAccessTokenStore(
    () => serviceProvider.GetRequiredService<IOptions<ZendeskOptions>>().Value.ApiAccessToken,
    "Zendesk:ApiAccessToken"));

var app = builder.Build();

// No HTTPS redirection: the callback arrives relayed by INTERA (or, with Zendesk:RedirectUri set, through nginx, where
// TLS ends), and the plugin calls this service over the internal network.
app.UseInteraApi(httpsRedirection: false);

app.Run();
