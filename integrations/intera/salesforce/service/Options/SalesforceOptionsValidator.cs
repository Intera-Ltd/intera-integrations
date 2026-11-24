using Intera.Common.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Intera.Salesforce.Api.Options;

public sealed class SalesforceOptionsValidator : IValidateOptions<SalesforceOptions>
{
    public ValidateOptionsResult Validate(string? name, SalesforceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Salesforce:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("Salesforce:ClientSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.LoginUrl))
        {
            failures.Add("Salesforce:LoginUrl must be configured.");
        }
        else if (!HttpsUriValidator.TryValidateHttpsOrLoopback(options.LoginUrl, out _, out var loginReason))
        {
            failures.Add($"Salesforce:LoginUrl {loginReason}");
        }

        if (!string.IsNullOrWhiteSpace(options.InstanceUrl)
            && !HttpsUriValidator.TryValidateHttpsOrLoopback(options.InstanceUrl, out _, out var instanceReason))
        {
            failures.Add($"Salesforce:InstanceUrl {instanceReason}");
        }

        if (string.IsNullOrWhiteSpace(options.ApiVersion))
        {
            failures.Add("Salesforce:ApiVersion must be configured.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
