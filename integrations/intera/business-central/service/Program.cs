using Intera.BusinessCentral.Api.Clients;
using Intera.BusinessCentral.Api.Options;
using Intera.BusinessCentral.Api.Services;
using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.Startup;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.BusinessCentral.Api");

builder.Services
    .AddOptions<BusinessCentralOptions>()
    .Bind(builder.Configuration.GetSection(BusinessCentralOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<BusinessCentralOptions>, BusinessCentralOptionsValidator>();

builder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BusinessCentralOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
});

// Singleton: the client-credentials grant has no refresh_token, so the token is cached
// in memory and re-acquired from Microsoft Entra ID when it expires.
builder.Services.AddSingleton<IBusinessCentralOAuthService, BusinessCentralOAuthService>();
builder.Services.AddScoped<IBusinessCentralClient, BusinessCentralClient>();

var app = builder.Build();

// No HTTPS redirection: this API is served over plain HTTP inside its deployment.
app.UseInteraApi(httpsRedirection: false);

app.Run();
