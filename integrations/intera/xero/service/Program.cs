using Intera.Common.Infrastructure.Startup;
using Intera.Xero.Api.Clients;
using Intera.Xero.Api.Infrastructure;
using Intera.Xero.Api.Options;
using Intera.Xero.Api.Services;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Xero.Api", requiresDataVolume: true);

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

// The grant rotates its refresh token on every use; both the encrypted OAuth token and the registered webhook
// redirect URL live on the persistent volume under /app/App_Data.
builder.Services.AddInteraFileOAuthStores(
    sp => sp.GetRequiredService<IOptionsMonitor<XeroOptions>>().CurrentValue.OAuthTokenFilePath,
    sp => sp.GetRequiredService<IOptionsMonitor<XeroOptions>>().CurrentValue.RedirectUriFilePath);

builder.Services.AddSingleton<XeroTokenService>();
builder.Services.AddSingleton<XeroResponseCache>();
builder.Services.AddSingleton<XeroClient>();
builder.Services.AddScoped<IXeroDataService, XeroDataService>();
builder.Services.AddHostedService<XeroTokenKeepAliveService>();

var app = builder.Build();

// No HTTPS redirection: the callback arrives relayed by INTERA (or through a reverse proxy when Xero:RedirectUri is
// set), and the plugin calls this service over the internal network.
app.UseInteraApi(httpsRedirection: false);

app.Run();
