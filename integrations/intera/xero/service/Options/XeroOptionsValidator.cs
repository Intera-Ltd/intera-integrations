using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Microsoft.Extensions.Options;

namespace Intera.Xero.Api.Options;

public sealed class XeroOptionsValidator : IValidateOptions<XeroOptions>
{
    public ValidateOptionsResult Validate(string? name, XeroOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Xero:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("Xero:ClientSecret must be configured.");
        }

        ValidateHttpsUrl(options.AuthorizeUrl, "Xero:AuthorizeUrl", failures);
        ValidateHttpsUrl(options.TokenUrl, "Xero:TokenUrl", failures);
        ValidateHttpsUrl(options.ApiBaseUrl, "Xero:ApiBaseUrl", failures);
        ValidateHttpsUrl(options.ConnectionsUrl, "Xero:ConnectionsUrl", failures);

        ValidateDataFilePath(options.OAuthTokenFilePath, "Xero:OAuthTokenFilePath", failures);
        ValidateDataFilePath(options.RedirectUriFilePath, "Xero:RedirectUriFilePath", failures);

        if (!string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            try
            {
                OAuthRedirectUriValidator.Validate(options.RedirectUri);
            }
            catch (ArgumentException exception)
            {
                failures.Add($"Xero:RedirectUri is invalid: {exception.Message}");
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

    private static void ValidateDataFilePath(string? path, string settingName, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            failures.Add($"{settingName} must be configured.");
            return;
        }

        var segments = path.Trim().Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            failures.Add($"{settingName} must not contain '..' path traversal segments.");
        }
    }
}
