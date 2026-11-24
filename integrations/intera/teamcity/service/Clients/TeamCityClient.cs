using Intera.Common.Infrastructure.Http;
using Intera.TeamCity.Api.Infrastructure;
using Intera.TeamCity.Api.Models;
using Intera.TeamCity.Api.Options;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Intera.TeamCity.Api.Clients;

public interface ITeamCityClient
{
    Task<IReadOnlyList<TeamCityBuildType>> GetBuildTypesAsync(int recentBuilds, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetRunningBuildTypeIdsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetQueuedBuildTypeIdsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamCityTestOccurrence>> GetTestOccurrencesAsync(long buildId, CancellationToken cancellationToken);
}

/// <summary>
/// Thin client over the TeamCity REST API. Builds locators, follows <c>nextHref</c>, and requires
/// bearer token authentication against <c>/app/rest</c>.
/// </summary>
public sealed partial class TeamCityClient(
    IOptions<TeamCityOptions> options,
    IJsonHttpClient httpClient,
    ILogger<TeamCityClient> logger) : ApiClientBase(httpClient), ITeamCityClient
{
    private const int PageSize = 1000;

    private const string BuildFields =
        "id,number,status,state,statusText,branchName,startDate,finishDate,webUrl," +
        "testOccurrences(count,passed,failed,ignored,muted,newFailed)";

    private const string TestOccurrenceFields =
        "id,name,status,duration,ignored,muted,currentlyMuted,currentlyInvestigated,newFailure,details,ignoreDetails," +
        "test(id,name,parsedTestName(testPackage,testSuite,testClass,testShortName,testMethodName))";

    private TeamCityOptions Options => options.Value;

    public async Task<IReadOnlyList<TeamCityBuildType>> GetBuildTypesAsync(int recentBuilds, CancellationToken cancellationToken)
    {
        var builds = Math.Clamp(recentBuilds, 1, 50);

        var fields =
            $"nextHref,buildType(id,name,projectId,projectName,webUrl,paused," +
            $"builds($locator(state:finished,count:{builds.ToString(CultureInfo.InvariantCulture)}),build({BuildFields})))";

        var locators = ScopeProjects().Count == 0
            ? ["templateFlag:false"]
            : ScopeProjects().Select(static project => $"affectedProject:(id:{project}),templateFlag:false").ToList();

        var result = new List<TeamCityBuildType>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var locator in locators)
        {
            var url = RestUrl("buildTypes", $"{locator},count:{PageSize}", fields);

            await foreach (var page in PagesAsync<TeamCityBuildTypeList>(url, static page => page.NextHref, cancellationToken).ConfigureAwait(false))
            {
                result.AddRange(page.BuildTypes.Where(buildType => seen.Add(buildType.Id)));
            }
        }

        return result;
    }

    public Task<IReadOnlyList<string>> GetRunningBuildTypeIdsAsync(CancellationToken cancellationToken)
    {
        return ReadBuildTypeIdsAsync(
            RestUrl("builds", $"state:running,defaultFilter:false,count:{PageSize}", "nextHref,build(buildTypeId)"),
            cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetQueuedBuildTypeIdsAsync(CancellationToken cancellationToken)
    {
        return ReadBuildTypeIdsAsync(
            RestUrl("buildQueue", $"count:{PageSize}", "nextHref,build(buildTypeId)"),
            cancellationToken);
    }

    public async Task<IReadOnlyList<TeamCityTestOccurrence>> GetTestOccurrencesAsync(long buildId, CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, Options.MaxTestsPerBuild);
        var url = RestUrl(
            "testOccurrences",
            $"build:(id:{buildId.ToString(CultureInfo.InvariantCulture)}),count:{PageSize}",
            $"nextHref,testOccurrence({TestOccurrenceFields})");

        var result = new List<TeamCityTestOccurrence>();

        await foreach (var page in PagesAsync<TeamCityTestOccurrenceList>(url, static page => page.NextHref, cancellationToken).ConfigureAwait(false))
        {
            result.AddRange(page.TestOccurrences);

            if (result.Count > limit || (result.Count == limit && page.NextHref is not null))
            {
                logger.LogWarning(
                    "Test occurrences of TeamCity build {BuildId} were truncated at {Limit}. Raise TeamCity:MaxTestsPerBuild.",
                    buildId,
                    limit);
                break;
            }
        }

        return result.Count > limit ? result.GetRange(0, limit) : result;
    }

    private async Task<IReadOnlyList<string>> ReadBuildTypeIdsAsync(string url, CancellationToken cancellationToken)
    {
        var result = new List<string>();

        await foreach (var page in PagesAsync<TeamCityBuildList>(url, static page => page.NextHref, cancellationToken).ConfigureAwait(false))
        {
            result.AddRange(page.Builds.Select(static build => build.BuildTypeId).OfType<string>());
        }

        return result;
    }

    private async IAsyncEnumerable<T> PagesAsync<T>(
        string firstUrl,
        Func<T, string?> nextHref,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        where T : class
    {
        var url = firstUrl;

        for (var page = 0; page < 1000 && url is not null; page++)
        {
            var response = await SendAsync<T>(url, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                yield break;
            }

            yield return response;

            var href = nextHref(response);
            url = string.IsNullOrWhiteSpace(href) ? null : ResolveHref(href);
        }
    }

    private async Task<T?> SendAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        ValidateOptions();

        try
        {
            return await HttpClient
                .GetAsync<T>(url, Options.Token, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpApiException exception)
        {
            throw TeamCityErrors.Translate(exception);
        }
    }

    private string RestUrl(string resource, string locator, string fields)
    {
        var serverUri = ValidateOptions();

        return $"{BuildUrl(serverUri.ToString(), $"app/rest/{resource}")}" +
            $"?locator={Uri.EscapeDataString(locator)}&fields={Uri.EscapeDataString(fields)}";
    }

    private string ResolveHref(string href)
    {
        var serverUri = ValidateOptions();
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            if (!string.Equals(absolute.Scheme, serverUri.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(absolute.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase)
                || absolute.Port != serverUri.Port)
            {
                throw new InvalidOperationException("TeamCity nextHref points to a different origin than TeamCity:ServerUrl.");
            }

            return absolute.ToString();
        }

        return new Uri(serverUri, href).ToString();
    }

    private IReadOnlyList<string> ScopeProjects()
    {
        var projects = Options.Projects
            .Select(static project => project?.Trim())
            .Where(static project => !string.IsNullOrEmpty(project))
            .Select(static project => project!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var project in projects)
        {
            if (!ProjectIdPattern().IsMatch(project))
            {
                throw new InvalidOperationException(
                    $"TeamCity:Projects contains '{project}', which is not a TeamCity project id (letters, digits and underscores; the id, not the display name).");
            }
        }

        return projects;
    }

    private Uri ValidateOptions()
    {
        var serverUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.ServerUrl, "TeamCity:ServerUrl");

        if (string.IsNullOrWhiteSpace(Options.Token))
        {
            throw new TeamCityAuthenticationException("TeamCity:Token is not configured.");
        }

        return serverUri;
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex ProjectIdPattern();
}
