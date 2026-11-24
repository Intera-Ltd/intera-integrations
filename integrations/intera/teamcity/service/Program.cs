using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.Startup;
using Intera.TeamCity.Api.Clients;
using Intera.TeamCity.Api.Options;
using Intera.TeamCity.Api.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.TeamCity.Api");

builder.Services
    .AddOptions<TeamCityOptions>()
    .Bind(builder.Configuration.GetSection(TeamCityOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<TeamCityOptions>, TeamCityOptionsValidator>();

builder.Services.AddHttpClient<IJsonHttpClient, JsonHttpClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<TeamCityOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
});

builder.Services.AddScoped<ITeamCityClient, TeamCityClient>();
builder.Services.AddScoped<IBuildTypeService, BuildTypeService>();
builder.Services.AddScoped<ITestResultService, TestResultService>();

var app = builder.Build();

// No HTTPS redirection: this API is served over plain HTTP inside its deployment.
app.UseInteraApi(httpsRedirection: false);

app.Run();
