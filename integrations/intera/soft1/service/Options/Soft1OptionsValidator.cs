using Intera.Common.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Intera.Soft1.Api.Options;

public sealed class Soft1OptionsValidator : IValidateOptions<Soft1Options>
{
    public ValidateOptionsResult Validate(string? name, Soft1Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            failures.Add("Soft1:ServiceUrl must be configured.");
        }
        else if (!HttpsUriValidator.TryValidateHttpsOrLoopback(options.ServiceUrl, out _, out var serviceUrlReason))
        {
            failures.Add($"Soft1:ServiceUrl {serviceUrlReason}");
        }

        if (string.IsNullOrWhiteSpace(options.Username))
        {
            failures.Add("Soft1:Username must be configured.");
        }

        if (string.IsNullOrEmpty(options.Password))
        {
            failures.Add("Soft1:Password must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.AppId))
        {
            failures.Add("Soft1:AppId must be configured.");
        }

        if (options.DefaultFiscalYears <= 0)
        {
            failures.Add("Soft1:DefaultFiscalYears must be greater than zero.");
        }

        if (options.MaxFiscalYears <= 0)
        {
            failures.Add("Soft1:MaxFiscalYears must be greater than zero.");
        }
        else if (options.DefaultFiscalYears > options.MaxFiscalYears)
        {
            failures.Add("Soft1:DefaultFiscalYears must not exceed Soft1:MaxFiscalYears.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
