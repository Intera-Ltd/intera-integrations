using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.Startup;
using Intera.Eurobank.Api.Clients;
using Intera.Eurobank.Api.Infrastructure;
using Intera.Eurobank.Api.Options;
using Intera.Eurobank.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Eurobank.Api", requiresDataVolume: true);

builder.Services
    .AddOptions<EurobankOptions>()
    .Bind(builder.Configuration.GetSection(EurobankOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<EurobankOptions>, EurobankOptionsValidator>();

builder.Services.AddTransient<EurobankClientCertificateHandler>();

builder.Services.AddHttpClient(EurobankOAuthService.HttpClientName, (serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<EurobankOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
})
    .ConfigurePrimaryHttpMessageHandler<EurobankClientCertificateHandler>();

builder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<EurobankOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
})
    .ConfigurePrimaryHttpMessageHandler<EurobankClientCertificateHandler>();

builder.Services.AddInteraFileOAuthStores(
    sp => sp.GetRequiredService<IOptionsMonitor<EurobankOptions>>().CurrentValue.OAuthTokenFilePath,
    sp => sp.GetRequiredService<IOptionsMonitor<EurobankOptions>>().CurrentValue.RedirectUriFilePath);

builder.Services.AddSingleton<IEurobankOAuthService, EurobankOAuthService>();
builder.Services.AddScoped<IEurobankAccountsClient, EurobankAccountsClient>();

var app = builder.Build();

app.UseInteraApi(httpsRedirection: false);

app.Run();
