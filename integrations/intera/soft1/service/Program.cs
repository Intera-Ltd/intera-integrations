using Intera.Common.Infrastructure.Startup;
using Intera.Soft1.Api.Clients;
using Intera.Soft1.Api.Infrastructure;
using Intera.Soft1.Api.Options;
using Intera.Soft1.Api.Services;
using Microsoft.Extensions.Options;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddInteraApi("Intera.Soft1.Api");

builder.Services
    .AddOptions<Soft1Options>()
    .Bind(builder.Configuration.GetSection(Soft1Options.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<Soft1Options>, Soft1OptionsValidator>();

// Not IJsonHttpClient: the body has to be decoded explicitly, since Soft1 may answer in windows-1253 — see Soft1Transport.
builder.Services.AddHttpClient<ISoft1Transport, Soft1Transport>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<Soft1Options>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(Math.Max(5, options.HttpTimeoutSeconds));
})
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

builder.Services.AddSingleton<Soft1SessionCache>();
builder.Services.AddSingleton<Soft1RequestGate>();
builder.Services.AddSingleton<Soft1ReadCatalog>();
builder.Services.AddScoped<ISoft1Client, Soft1Client>();
builder.Services.AddScoped<ISoft1Reader, Soft1Reader>();
builder.Services.AddScoped<ISoft1DataService, Soft1DataService>();

var app = builder.Build();

// No HTTPS redirection: this API is served over plain HTTP inside its deployment.
app.UseInteraApi(httpsRedirection: false);

app.Run();
