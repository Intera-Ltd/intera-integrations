using Intera.Common.Authentication;
using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.Startup;
using Intera.ZohoInventory.Api.Clients;
using Intera.ZohoInventory.Api.Infrastructure;
using Intera.ZohoInventory.Api.Options;
using Intera.ZohoInventory.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.ZohoInventory.Api");

builder.Services
    .AddOptions<ZohoInventoryOptions>()
    .Bind(builder.Configuration.GetSection(ZohoInventoryOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ZohoInventoryOptions>, ZohoInventoryOptionsValidator>();

builder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ZohoInventoryOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
});

// Singleton so the cached access token and its refresh gate are shared, exactly as
// SalesforceOAuthService is: the credential is per deployment, so there is one token for the process.
builder.Services.AddSingleton<IZohoOAuthService, ZohoOAuthService>();

// Singleton for the same reason in the other direction: Zoho's per-minute limit belongs to the
// organization, so every request this process serves has to draw on one budget.
builder.Services.AddSingleton<ZohoRequestThrottle>();

builder.Services.AddScoped<IZohoInventoryClient, ZohoInventoryClient>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<ISchemaService, SchemaService>();

builder.Services.AddSingleton<IAccessTokenStore>(serviceProvider => new StaticAccessTokenStore(
    () => serviceProvider.GetRequiredService<IOptions<ZohoInventoryOptions>>().Value.ApiAccessToken,
    "ZohoInventory:ApiAccessToken"));

var app = builder.Build();

// No HTTPS redirection: this API is served over plain HTTP inside its deployment.
app.UseInteraApi(httpsRedirection: false);

app.Run();
