using Intera.Common.Authentication;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Infrastructure.Startup;
using Intera.Xero.Api.Clients;
using Intera.Xero.Api.Infrastructure;
using Intera.Xero.Api.Options;
using Intera.Xero.Api.Services;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Xero.Api");

builder.Services
    .AddOptions<XeroOptions>()
    .Bind(builder.Configuration.GetSection(XeroOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<XeroOptions>, XeroOptionsValidator>();

// One named client for the identity server and the API alike; the Singletons below take it from the factory per
// call, so no handler is pinned for the process lifetime.
builder.Services.AddHttpClient(XeroTokenService.HttpClientName, (serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<XeroOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

// The grant rotates its refresh token on every use, so it lives in this service's own file — see XeroTokenService.
builder.Services.AddSingleton<IOAuthTokenStore>(serviceProvider => new FileOAuthTokenStore(
    () => serviceProvider.GetRequiredService<IOptionsMonitor<XeroOptions>>().CurrentValue.OAuthTokenFilePath));

// The redirect URL INTERA registers (its webhook address) is persisted alongside the grant.
builder.Services.AddSingleton<IOAuthRedirectUriStore>(serviceProvider => new FileOAuthRedirectUriStore(
    () => serviceProvider.GetRequiredService<IOptionsMonitor<XeroOptions>>().CurrentValue.RedirectUriFilePath));

builder.Services.AddSingleton<XeroTokenService>();
builder.Services.AddSingleton<XeroResponseCache>();
builder.Services.AddSingleton<XeroClient>();
builder.Services.AddScoped<IXeroDataService, XeroDataService>();
builder.Services.AddHostedService<XeroTokenKeepAliveService>();

builder.Services.AddSingleton<IAccessTokenStore>(serviceProvider => new StaticAccessTokenStore(
    () => serviceProvider.GetRequiredService<IOptions<XeroOptions>>().Value.ApiAccessToken,
    "Xero:ApiAccessToken"));

var app = builder.Build();

// No HTTPS redirection: the callback arrives relayed by INTERA (or through a reverse proxy when Xero:RedirectUri is
// set), and the plugin calls this service over the internal network.
app.UseInteraApi(httpsRedirection: false);

app.Run();
