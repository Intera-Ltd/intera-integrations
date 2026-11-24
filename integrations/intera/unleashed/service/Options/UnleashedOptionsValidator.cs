using Intera.Common.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Intera.Unleashed.Api.Options;

public sealed class UnleashedOptionsValidator : IValidateOptions<UnleashedOptions>
{
    public ValidateOptionsResult Validate(string? name, UnleashedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ApiId))
        {
            failures.Add("Unleashed:ApiId must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add("Unleashed:ApiKey must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientType))
        {
            failures.Add("Unleashed:ClientType must be configured.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiBaseUrl))
        {
            failures.Add("Unleashed:ApiBaseUrl must be configured.");
        }
        else if (!HttpsUriValidator.TryValidateHttpsOrLoopback(options.ApiBaseUrl, out _, out var baseUrlReason))
        {
            failures.Add($"Unleashed:ApiBaseUrl {baseUrlReason}");
        }

        if (options.PageSize is < 1 or > 1000)
        {
            failures.Add("Unleashed:PageSize must be between 1 and 1000.");
        }

        if (options.MaxPages <= 0)
        {
            failures.Add("Unleashed:MaxPages must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
