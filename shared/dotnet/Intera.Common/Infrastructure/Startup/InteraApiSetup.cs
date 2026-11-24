using Intera.Common.Authentication;
using Intera.Common.Infrastructure.OAuth;
using Intera.Common.Infrastructure.Options;
using Intera.Common.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Serilog;
using System.Text.Json;

namespace Intera.Common.Infrastructure.Startup;

/// <summary>
/// Shared ASP.NET Core bootstrap for INTERA .NET integration services.
/// Logs to stdout/stderr only, requires <c>INTERA_SERVICE_TOKEN</c> on every route except <c>GET /health</c>,
/// and validates <c>INTERA_SERVICE_DATA_KEY</c> when <paramref name="requiresDataVolume"/> is enabled.
/// </summary>
public static class InteraApiSetup
{
    public static WebApplicationBuilder AddInteraApi(
        this WebApplicationBuilder builder,
        string title,
        bool requiresDataVolume = false)
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
            .AddOptions<ServiceSecretsOptions>()
            .Configure<IConfiguration>((opts, config) =>
            {
                opts.ServiceToken = config[ServiceSecretsOptions.ServiceTokenEnvironmentVariable] ?? string.Empty;
                opts.DataKey = config[ServiceSecretsOptions.DataKeyEnvironmentVariable] ?? string.Empty;
                opts.RequiresDataKey = requiresDataVolume;
            })
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ServiceSecretsOptions>, ServiceSecretsOptionsValidator>();

        builder.Services.AddSingleton<IAccessTokenStore>(serviceProvider => new StaticAccessTokenStore(
            () => serviceProvider.GetRequiredService<IOptionsMonitor<ServiceSecretsOptions>>().CurrentValue.ServiceToken,
            ServiceSecretsOptions.ServiceTokenEnvironmentVariable));

        if (requiresDataVolume)
        {
            builder.Services.AddSingleton(serviceProvider =>
            {
                var raw = serviceProvider.GetRequiredService<IOptions<ServiceSecretsOptions>>().Value.DataKey;
                if (!ServiceDataKey.TryDecode(raw, out var keyBytes))
                {
                    throw new InvalidOperationException(
                        $"{ServiceSecretsOptions.DataKeyEnvironmentVariable} is missing or invalid.");
                }

                return new ServiceDataKey(keyBytes);
            });
        }

        builder.Services
            .AddAuthentication(AccessTokenAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, AccessTokenAuthenticationHandler>(
                AccessTokenAuthenticationDefaults.Scheme,
                options => { });

        var requireServiceTokenPolicy = new AuthorizationPolicyBuilder(AccessTokenAuthenticationDefaults.Scheme)
            .RequireAuthenticatedUser()
            .Build();

        builder.Services
            .AddAuthorizationBuilder()
            .SetDefaultPolicy(requireServiceTokenPolicy)
            .SetFallbackPolicy(requireServiceTokenPolicy);

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

    public static IServiceCollection AddInteraFileOAuthStores(
        this IServiceCollection services,
        Func<IServiceProvider, string> tokenFilePathResolver,
        Func<IServiceProvider, string>? redirectUriFilePathResolver = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tokenFilePathResolver);

        services.AddSingleton<IOAuthTokenStore>(serviceProvider => new FileOAuthTokenStore(
            () => tokenFilePathResolver(serviceProvider),
            serviceProvider.GetRequiredService<ServiceDataKey>()));

        if (redirectUriFilePathResolver is not null)
        {
            services.AddSingleton<IOAuthRedirectUriStore>(serviceProvider => new FileOAuthRedirectUriStore(
                () => redirectUriFilePathResolver(serviceProvider)));
        }

        return services;
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

        app.MapGet("/health", () => Results.Ok(new { status = "ok", utc_now = DateTimeOffset.UtcNow }))
            .AllowAnonymous();
        app.MapControllers();

        return app;
    }
}
