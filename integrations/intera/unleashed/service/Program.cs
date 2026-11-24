using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.Startup;
using Intera.Unleashed.Api.Clients;
using Intera.Unleashed.Api.Options;
using Intera.Unleashed.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Unleashed.Api");

builder.Services
    .AddOptions<UnleashedOptions>()
    .Bind(builder.Configuration.GetSection(UnleashedOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<UnleashedOptions>, UnleashedOptionsValidator>();

builder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<UnleashedOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
});

builder.Services.AddMemoryCache();

// Unleashed authenticates each request via HMAC-SHA256 over its query string using Unleashed:ApiKey.
builder.Services.AddScoped<IUnleashedClient, UnleashedClient>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<IStockOnHandService, StockOnHandService>();
builder.Services.AddScoped<IDocumentQueryService, DocumentQueryService>();

var app = builder.Build();

// No HTTPS redirection: this API is served over plain HTTP inside its deployment.
app.UseInteraApi(httpsRedirection: false);

app.Run();
