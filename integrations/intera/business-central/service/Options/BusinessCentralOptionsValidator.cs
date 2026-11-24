using Intera.Common.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Intera.BusinessCentral.Api.Options;

public sealed class BusinessCentralOptionsValidator : IValidateOptions<BusinessCentralOptions>
{
    public ValidateOptionsResult Validate(string? name, BusinessCentralOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            failures.Add("BusinessCentral:TenantId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Environment))
        {
            failures.Add("BusinessCentral:Environment must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("BusinessCentral:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("BusinessCentral:ClientSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.Scope))
        {
            failures.Add("BusinessCentral:Scope must be configured.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiBaseUrl)
            && !HttpsUriValidator.TryValidateHttpsOrLoopback(options.ApiBaseUrl, out _, out var apiBaseReason))
        {
            failures.Add($"BusinessCentral:ApiBaseUrl {apiBaseReason}");
        }

        if (!string.IsNullOrWhiteSpace(options.TokenEndpoint)
            && !HttpsUriValidator.TryValidateHttpsOrLoopback(options.TokenEndpoint, out _, out var tokenReason))
        {
            failures.Add($"BusinessCentral:TokenEndpoint {tokenReason}");
        }

        if (options.MaxRecordsPerEntity <= 0)
        {
            failures.Add("BusinessCentral:MaxRecordsPerEntity must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
