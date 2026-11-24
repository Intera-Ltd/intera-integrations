using Intera.Common.Infrastructure.OAuth;
using Intera.Zendesk.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Intera.Zendesk.Api.Options;

public sealed class ZendeskOptionsValidator : IValidateOptions<ZendeskOptions>
{
    public ValidateOptionsResult Validate(string? name, ZendeskOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Zendesk:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("Zendesk:ClientSecret must be configured.");
        }

        try
        {
            ZendeskBaseUri.Resolve(options);
        }
        catch (InvalidOperationException exception)
        {
            failures.Add(exception.Message);
        }

        ValidateDataFilePath(options.OAuthTokenFilePath, "Zendesk:OAuthTokenFilePath", failures);
        ValidateDataFilePath(options.RedirectUriFilePath, "Zendesk:RedirectUriFilePath", failures);

        if (!string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            try
            {
                OAuthRedirectUriValidator.Validate(options.RedirectUri);
            }
            catch (ArgumentException exception)
            {
                failures.Add($"Zendesk:RedirectUri is invalid: {exception.Message}");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
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
