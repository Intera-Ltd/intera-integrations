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
}
