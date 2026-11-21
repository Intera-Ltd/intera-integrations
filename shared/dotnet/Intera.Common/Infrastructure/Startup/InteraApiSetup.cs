using Intera.Common.Authentication;
using Intera.Common.Infrastructure.Options;
using Intera.Common.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using System.Text.Json;

namespace Intera.Common.Infrastructure.Startup;

/// <summary>
/// Shared ASP.NET Core bootstrap for INTERA .NET integration services.
/// Logs to stdout/stderr only (stateless container standard) and exposes <c>GET /health</c>
/// alongside controller routes.
/// </summary>
public static class InteraApiSetup
{
    public static WebApplicationBuilder AddInteraApi(this WebApplicationBuilder builder, string title)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console();
        });

        builder.Services.Configure<ApiOptions>(
            builder.Configuration.GetSection(ApiOptions.SectionName));

        builder.Services
            .AddAuthentication(AccessTokenAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, AccessTokenAuthenticationHandler>(
                AccessTokenAuthenticationDefaults.Scheme,
                options => { });

        builder.Services.AddAuthorization();

        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<ApiExceptionFilter>();
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        });

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = title,
                Version = "v1"
            });

            options.OperationFilter<SwaggerManualTokenOperationFilter>();
        });

        return builder;
    }

    public static WebApplication UseInteraApi(this WebApplication app, bool httpsRedirection = false)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSerilogRequestLogging();

        var apiOptions = app.Services.GetRequiredService<IOptions<ApiOptions>>().Value;
        if (apiOptions.EnableSwagger ?? app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (httpsRedirection)
        {
            app.UseHttpsRedirection();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "ok", utc_now = DateTimeOffset.UtcNow }));
        app.MapControllers();

        return app;
    }
}
