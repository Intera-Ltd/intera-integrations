using System.Text.RegularExpressions;
using Intera.Common.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Intera.TeamCity.Api.Options;

public sealed partial class TeamCityOptionsValidator : IValidateOptions<TeamCityOptions>
{
    public ValidateOptionsResult Validate(string? name, TeamCityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ServerUrl))
        {
            failures.Add("TeamCity:ServerUrl must be configured.");
        }
        else if (!HttpsUriValidator.TryValidateHttpsOrLoopback(options.ServerUrl, out _, out var serverUrlReason))
        {
            failures.Add($"TeamCity:ServerUrl {serverUrlReason}");
        }

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            failures.Add("TeamCity:Token must be configured.");
        }

        if (options.TestBuildLookback <= 0)
        {
            failures.Add("TeamCity:TestBuildLookback must be greater than zero.");
        }

        if (options.MaxBuildTypes <= 0)
        {
            failures.Add("TeamCity:MaxBuildTypes must be greater than zero.");
        }

        if (options.MaxTestsPerBuild <= 0)
        {
            failures.Add("TeamCity:MaxTestsPerBuild must be greater than zero.");
        }

        foreach (var rawProject in options.Projects ?? [])
        {
            var project = rawProject?.Trim();
            if (!string.IsNullOrEmpty(project) && !ProjectIdPattern().IsMatch(project))
            {
                failures.Add($"TeamCity:Projects contains '{project}', which is not a valid TeamCity project id (letters, digits, and underscores).");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex ProjectIdPattern();
}
