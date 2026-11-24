using Intera.Common.Infrastructure.Startup;
using Intera.Zendesk.Api.Clients;
using Intera.Zendesk.Api.Infrastructure;
using Intera.Zendesk.Api.Options;
using Intera.Zendesk.Api.Services;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Zendesk.Api", requiresDataVolume: true);

builder.Services
    .AddOptions<ZendeskOptions>()
    .Bind(builder.Configuration.GetSection(ZendeskOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ZendeskOptions>, ZendeskOptionsValidator>();

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

// The grant rotates its refresh token on every use; both the encrypted OAuth token and the registered webhook
// redirect URL live on the persistent volume under /app/App_Data.
builder.Services.AddInteraFileOAuthStores(
    sp => sp.GetRequiredService<IOptionsMonitor<ZendeskOptions>>().CurrentValue.OAuthTokenFilePath,
    sp => sp.GetRequiredService<IOptionsMonitor<ZendeskOptions>>().CurrentValue.RedirectUriFilePath);

builder.Services.AddSingleton<ZendeskTokenService>();
builder.Services.AddSingleton<ZendeskResponseCache>();
builder.Services.AddSingleton<ZendeskClient>();
builder.Services.AddScoped<IZendeskDataService, ZendeskDataService>();
builder.Services.AddHostedService<ZendeskTokenKeepAliveService>();

var app = builder.Build();

// No HTTPS redirection: the callback arrives relayed by INTERA (or, with Zendesk:RedirectUri set, through nginx, where
// TLS ends), and the plugin calls this service over the internal network.
app.UseInteraApi(httpsRedirection: false);

app.Run();
