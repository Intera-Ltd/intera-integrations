using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Microsoft.Extensions.Options;

namespace Intera.Eurobank.Api.Options;

public sealed class EurobankOptionsValidator : IValidateOptions<EurobankOptions>
{
    public ValidateOptionsResult Validate(string? name, EurobankOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Eurobank:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("Eurobank:ClientSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Scope))
        {
            failures.Add("Eurobank:Scope must be configured.");
        }

        ValidateHttpsUrl(options.ApiBaseUrl, "Eurobank:ApiBaseUrl", failures);
        ValidateHttpsUrl(options.AuthBaseUrl, "Eurobank:AuthBaseUrl", failures);

        if (!string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            try
            {
                OAuthRedirectUriValidator.Validate(options.RedirectUri);
            }
            catch (ArgumentException exception)
            {
                failures.Add($"Eurobank:RedirectUri is invalid: {exception.Message}");
            }
        }

        ValidateStoragePath(options.OAuthTokenFilePath, "Eurobank:OAuthTokenFilePath", failures);
        ValidateStoragePath(options.RedirectUriFilePath, "Eurobank:RedirectUriFilePath", failures);

        var hasCertificatePem = !string.IsNullOrWhiteSpace(options.ClientCertificatePem);
        var hasPrivateKeyPem = !string.IsNullOrWhiteSpace(options.ClientPrivateKeyPem);
        if (hasCertificatePem != hasPrivateKeyPem)
        {
            failures.Add(
                "Eurobank:ClientCertificatePem and Eurobank:ClientPrivateKeyPem must both be configured together when using PEM mTLS credentials.");
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
