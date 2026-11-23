using Intera.Common.Infrastructure.Http;
using Intera.ZohoInventory.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace Intera.ZohoInventory.Api.Options;

public sealed class ZohoInventoryOptionsValidator : IValidateOptions<ZohoInventoryOptions>
{
    public ValidateOptionsResult Validate(string? name, ZohoInventoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.OrganizationId))
        {
            failures.Add("ZohoInventory:OrganizationId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("ZohoInventory:ClientId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("ZohoInventory:ClientSecret must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            failures.Add("ZohoInventory:RefreshToken must be configured.");
        }

        try
        {
            var accountsUrl = ZohoDataCenter.ResolveAccountsUrl(options);
            if (!HttpsUriValidator.TryValidateHttpsOrLoopback(accountsUrl, out _, out var accountsReason))
            {
                failures.Add($"ZohoInventory:AccountsUrl {accountsReason}");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ZohoAuthenticationException)
        {
            failures.Add(exception.Message);
        }

        if (!string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            if (!HttpsUriValidator.TryValidateHttpsOrLoopback(options.ApiBaseUrl, out _, out var apiReason))
            {
                failures.Add($"ZohoInventory:ApiBaseUrl {apiReason}");
            }
        }
        else
        {
            try
            {
                var defaultApiUrl = ZohoDataCenter.ResolveDefaultApiBaseUrl(options);
                if (!HttpsUriValidator.TryValidateHttpsOrLoopback(defaultApiUrl, out _, out var defaultApiReason))
                {
                    failures.Add($"ZohoInventory:ApiBaseUrl {defaultApiReason}");
                }
            }
            catch (ZohoAuthenticationException exception)
            {
                if (!failures.Contains(exception.Message, StringComparer.Ordinal))
                {
                    failures.Add(exception.Message);
                }
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
