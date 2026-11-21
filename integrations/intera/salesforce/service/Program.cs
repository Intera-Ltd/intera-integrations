using Intera.Common.Authentication;
using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.Startup;
using Intera.Salesforce.Api.Clients;
using Intera.Salesforce.Api.Options;
using Intera.Salesforce.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Salesforce.Api");

builder.Services.Configure<SalesforceOptions>(builder.Configuration.GetSection(SalesforceOptions.SectionName));

builder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SalesforceOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
});

// Singleton so the cached access token and its refresh gate are shared across requests.
// The token is per deployment rather than per caller, so there is one instance for the process.
builder.Services.AddSingleton<ISalesforceOAuthService, SalesforceOAuthService>();

builder.Services.AddScoped<ISalesforceClient, SalesforceClient>();
builder.Services.AddScoped<ISObjectQueryService, SObjectQueryService>();

builder.Services.AddSingleton<IAccessTokenStore>(serviceProvider => new StaticAccessTokenStore(
    () => serviceProvider.GetRequiredService<IOptions<SalesforceOptions>>().Value.ApiAccessToken,
    "Salesforce:ApiAccessToken"));

var app = builder.Build();

// No HTTPS redirection: this API is served over plain HTTP inside its deployment.
app.UseInteraApi(httpsRedirection: false);

app.Run();
