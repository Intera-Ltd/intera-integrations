using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.Startup;
using Intera.Revolut.Api.Clients;
using Intera.Revolut.Api.Options;
using Intera.Revolut.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Revolut.Api", requiresDataVolume: true);

builder.Services
    .AddOptions<RevolutOptions>()
    .Bind(builder.Configuration.GetSection(RevolutOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RevolutOptions>, RevolutOptionsValidator>();

builder.Services.AddHttpClient(RevolutOAuthService.HttpClientName, (serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<RevolutOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
});

builder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<RevolutOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
});

builder.Services.AddInteraFileOAuthStores(
    sp => sp.GetRequiredService<IOptionsMonitor<RevolutOptions>>().CurrentValue.OAuthTokenFilePath,
    sp => sp.GetRequiredService<IOptionsMonitor<RevolutOptions>>().CurrentValue.RedirectUriFilePath);

builder.Services.AddSingleton<Intera.Common.Infrastructure.OAuth.IClientAssertionFactory, Intera.Common.Infrastructure.OAuth.RsaJwtClientAssertionFactory>();
builder.Services.AddSingleton<IRevolutOAuthService, RevolutOAuthService>();
builder.Services.AddScoped<IRevolutBusinessClient, RevolutBusinessClient>();

var app = builder.Build();

app.UseInteraApi(httpsRedirection: false);

app.Run();
