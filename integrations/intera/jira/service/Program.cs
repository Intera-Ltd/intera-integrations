using Intera.Common.Authentication;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Infrastructure.Startup;
using Intera.Jira.Api.Clients;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Options;
using Intera.Jira.Api.Services;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Jira.Api");

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

// The grant rotates its refresh token on every use, so it lives in this service's own file — see JiraTokenService.
builder.Services.AddSingleton<IOAuthTokenStore>(serviceProvider => new FileOAuthTokenStore(
    () => serviceProvider.GetRequiredService<IOptionsMonitor<JiraOptions>>().CurrentValue.OAuthTokenFilePath));

// The redirect URL INTERA registers (its webhook address) is persisted alongside the grant.
builder.Services.AddSingleton<IOAuthRedirectUriStore>(serviceProvider => new FileOAuthRedirectUriStore(
    () => serviceProvider.GetRequiredService<IOptionsMonitor<JiraOptions>>().CurrentValue.RedirectUriFilePath));

builder.Services.AddSingleton<JiraTokenService>();
builder.Services.AddSingleton<JiraResponseCache>();
builder.Services.AddSingleton<JiraClient>();
builder.Services.AddScoped<IJiraDataService, JiraDataService>();
builder.Services.AddHostedService<JiraTokenKeepAliveService>();

builder.Services.AddSingleton<IAccessTokenStore>(serviceProvider => new StaticAccessTokenStore(
    () => serviceProvider.GetRequiredService<IOptions<JiraOptions>>().Value.ApiAccessToken,
    "Jira:ApiAccessToken"));

var app = builder.Build();

// No HTTPS redirection: the callback arrives relayed by INTERA (or through a reverse proxy when Jira:RedirectUri is
// set), and the plugin calls this service over the internal network.
app.UseInteraApi(httpsRedirection: false);

app.Run();
