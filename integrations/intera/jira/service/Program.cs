using Intera.Common.Infrastructure.Startup;
using Intera.Jira.Api.Clients;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Options;
using Intera.Jira.Api.Services;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Jira.Api", requiresDataVolume: true);

builder.Services
    .AddOptions<JiraOptions>()
    .Bind(builder.Configuration.GetSection(JiraOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JiraOptions>, JiraOptionsValidator>();

// One named client for the Atlassian auth server and the API gateway alike; the Singletons below take it from the
// factory per call, so no handler is pinned for the process lifetime.
builder.Services.AddHttpClient(JiraTokenService.HttpClientName, (serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<JiraOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

// The grant rotates its refresh token on every use; both the encrypted OAuth token and the registered webhook
// redirect URL live on the persistent volume under /app/App_Data.
builder.Services.AddInteraFileOAuthStores(
    sp => sp.GetRequiredService<IOptionsMonitor<JiraOptions>>().CurrentValue.OAuthTokenFilePath,
    sp => sp.GetRequiredService<IOptionsMonitor<JiraOptions>>().CurrentValue.RedirectUriFilePath);

builder.Services.AddSingleton<JiraTokenService>();
builder.Services.AddSingleton<JiraResponseCache>();
builder.Services.AddSingleton<JiraClient>();
builder.Services.AddScoped<IJiraDataService, JiraDataService>();
builder.Services.AddHostedService<JiraTokenKeepAliveService>();

var app = builder.Build();

// No HTTPS redirection: the callback arrives relayed by INTERA (or through a reverse proxy when Jira:RedirectUri is
// set), and the plugin calls this service over the internal network.
app.UseInteraApi(httpsRedirection: false);

app.Run();
