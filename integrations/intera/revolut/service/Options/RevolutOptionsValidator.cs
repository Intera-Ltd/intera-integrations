using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Microsoft.Extensions.Options;

namespace Intera.Revolut.Api.Options;

public sealed class RevolutOptionsValidator : IValidateOptions<RevolutOptions>
{
    public ValidateOptionsResult Validate(string? name, RevolutOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Revolut:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientAssertionIssuer))
        {
            failures.Add("Revolut:ClientAssertionIssuer must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.PrivateKeyPem) && string.IsNullOrWhiteSpace(options.PrivateKeyPath))
        {
            failures.Add("Revolut:PrivateKeyPem (or Revolut:PrivateKeyPath) must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientAssertionAudience))
        {
            failures.Add("Revolut:ClientAssertionAudience must be configured.");
        }

        ValidateHttpsUrl(options.ApiBaseUrl, "Revolut:ApiBaseUrl", failures);
        ValidateHttpsUrl(options.BusinessPortalBaseUrl, "Revolut:BusinessPortalBaseUrl", failures);
        ValidateHttpsUrl(options.TokenEndpoint, "Revolut:TokenEndpoint", failures);

        if (!string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            try
            {
                OAuthRedirectUriValidator.Validate(options.RedirectUri);
            }
            catch (ArgumentException exception)
            {
                failures.Add($"Revolut:RedirectUri is invalid: {exception.Message}");
            }
        }

        ValidateStoragePath(options.OAuthTokenFilePath, "Revolut:OAuthTokenFilePath", failures);
        ValidateStoragePath(options.RedirectUriFilePath, "Revolut:RedirectUriFilePath", failures);

        if (options.TransactionsLookbackDays <= 0)
        {
            failures.Add("Revolut:TransactionsLookbackDays must be greater than zero.");
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

    private static void ValidateStoragePath(string? path, string settingName, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            failures.Add($"{settingName} must be configured.");
            return;
        }

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Contains("..", StringComparer.Ordinal))
        {
            failures.Add($"{settingName} must not contain '..' path traversal segments.");
        }
    }
}
