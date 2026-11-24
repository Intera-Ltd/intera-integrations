using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Intera.Jira.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Intera.Jira.Api.Options;

public sealed class JiraOptionsValidator : IValidateOptions<JiraOptions>
{
    public ValidateOptionsResult Validate(string? name, JiraOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Jira:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("Jira:ClientSecret must be configured.");
        }

        ValidateHttpsUrl(options.AuthorizeUrl, "Jira:AuthorizeUrl", failures);
        ValidateHttpsUrl(options.TokenUrl, "Jira:TokenUrl", failures);
        ValidateHttpsUrl(options.ApiGatewayUrl, "Jira:ApiGatewayUrl", failures);
        ValidateHttpsUrl(options.AccessibleResourcesUrl, "Jira:AccessibleResourcesUrl", failures);

        if (!string.IsNullOrWhiteSpace(options.SiteUrl))
        {
            var site = options.SiteUrl.Trim();
            if (site.Contains("://", StringComparison.Ordinal)
                || site.StartsWith("http:", StringComparison.OrdinalIgnoreCase)
                || site.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            {
                ValidateHttpsUrl(site, "Jira:SiteUrl", failures);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            try
            {
                OAuthRedirectUriValidator.Validate(options.RedirectUri);
            }
            catch (ArgumentException exception)
            {
                failures.Add($"Jira:RedirectUri is invalid: {exception.Message}");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.JqlScope))
        {
            try
            {
                Jql.ValidateScope(options.JqlScope, "Jira:JqlScope");
            }
            catch (JiraRequestException exception)
            {
                failures.Add(exception.Message);
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateHttpsUrl(string? url, string settingName, List<string> failures)
    {
        if (!HttpsUriValidator.TryValidateHttpsOrLoopback(url, out _, out var reason))
        {
            failures.Add($"{settingName} {reason}");
        }
    }
}
