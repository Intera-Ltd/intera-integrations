using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.TeamCity.Api.Clients;
using Intera.TeamCity.Api.Infrastructure;
using Intera.TeamCity.Api.Models;
using Intera.TeamCity.Api.Models.Query;
using Intera.TeamCity.Api.Options;
using Microsoft.Extensions.Options;

namespace Intera.TeamCity.Api.Services;

public interface IBuildTypeService
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One row per build configuration with the state of its newest finished build — what TeamCity's sidebar
/// shows as a green or red square — plus how many of its builds are running and queued right now.
/// </summary>
/// <remarks>
/// Three upstream calls however many configurations there are: the configuration list carries its latest
/// build inline, and the running and queued builds are read once for the whole server and counted per
/// configuration here.
/// </remarks>
public sealed class BuildTypeService(ITeamCityClient client) : IBuildTypeService
{
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(CancellationToken cancellationToken)
    {
        var buildTypesTask = client.GetBuildTypesAsync(1, cancellationToken);
        var runningTask = client.GetRunningBuildTypeIdsAsync(cancellationToken);
        var queuedTask = client.GetQueuedBuildTypeIdsAsync(cancellationToken);

        await Task.WhenAll(buildTypesTask, runningTask, queuedTask).ConfigureAwait(false);

        var running = CountById(runningTask.Result);
        var queued = CountById(queuedTask.Result);

        return
        [
            .. buildTypesTask.Result.Select(buildType => TeamCityRows.BuildType(
                buildType,
                running.GetValueOrDefault(buildType.Id),
                queued.GetValueOrDefault(buildType.Id)))
        ];
    }

    private static Dictionary<string, int> CountById(IReadOnlyList<string> buildTypeIds)
    {
        return buildTypeIds
            .GroupBy(static id => id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
    }
}

public interface ITestResultService
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(QueryRequest? query, CancellationToken cancellationToken);
}

/// <summary>
/// One row per test in the newest finished build of each configuration that reported tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this grain and not "the test's own last run".</b> TeamCity can only find a test by its exact
/// name (measured on 2026.2: <c>/tests</c> refuses a <c>matchType</c> locator, and
/// <c>testOccurrences</c> needs a build or an exact test), so a filter written as <c>LIKE</c> — which is
/// what picking a test in a dashboard produces — has nothing to push down to. Reading the tests of one
/// build per configuration answers every filter the same way. The value in a row is always the test's
/// real latest result; what the grain gives up is a test that did not run in that build at all, which
/// is then absent rather than stale.
/// </para>
/// <para>
/// <b>Why "that reported tests".</b> A build that failed before its test step reports none, and taking
/// the newest build blindly would empty a test dashboard exactly when something is broken. Up to
/// <c>TeamCity:TestBuildLookback</c> builds back are considered, and <c>from_latest_build</c> says
/// whether the row came from the configuration's newest build or an earlier one.
/// </para>
/// <para>
/// Conditions on the configuration's own columns are evaluated against the configuration list first, so
/// a query about one project or one configuration reads only its tests. Everything else is filtered in
/// memory afterwards, like every in-memory source here.
/// </para>
/// </remarks>
public sealed class TestResultService(
    ITeamCityClient client,
    IOptions<TeamCityOptions> options) : ITestResultService
{
    private const string IndexKey = "__build_type_index";

    private TeamCityOptions Options => options.Value;

    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(QueryRequest? query, CancellationToken cancellationToken)
    {
        var pushdown = QueryPushdownExtractor.Extract(query, dateField: null, keyFields: [], localFields: TeamCityRows.BuildTypeColumns);

        var catalog = await client.GetBuildTypesAsync(Math.Max(1, Options.TestBuildLookback), cancellationToken).ConfigureAwait(false);
        var selected = Restrict(catalog, pushdown);

        var targets = selected
            .Select(static buildType => (BuildType: buildType, Build: SelectTestBuild(buildType)))
            .Where(static target => target.Build is not null)
            .ToList();

        var maxBuildTypes = Math.Max(1, Options.MaxBuildTypes);
        if (targets.Count > maxBuildTypes)
        {
            throw new TeamCityQueryTooBroadException(
                $"This query would read the tests of {targets.Count} build configurations, more than TeamCity:MaxBuildTypes ({maxBuildTypes}). " +
                "Narrow it with a condition on project_id, project_name, build_type_id or build_type_name.");
        }

        var buckets = new IReadOnlyList<IReadOnlyDictionary<string, object?>>[targets.Count];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, targets.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Options.MaxParallelRequests),
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                var (buildType, build) = targets[index];
                var latest = buildType.Builds!.Builds[0];
                var occurrences = await client.GetTestOccurrencesAsync(build!.Id, token).ConfigureAwait(false);

                buckets[index] =
                [
                    .. occurrences.Select(occurrence => TeamCityRows.TestResult(buildType, build, build.Id == latest.Id, occurrence))
                ];
            }).ConfigureAwait(false);

        // Kept in catalog order so top means the same thing from one call to the next.
        return [.. buckets.SelectMany(static bucket => bucket)];
    }

    /// <summary>The newest of the recent builds that reported at least one test.</summary>
    private static TeamCityBuild? SelectTestBuild(TeamCityBuildType buildType)
    {
        return buildType.Builds?.Builds.FirstOrDefault(static build => build.TestOccurrences?.Count > 0);
    }

    /// <summary>
    /// Applies the configuration-column conditions from the filter's top-level AND chain to the list,
    /// through <see cref="InMemoryQueryEngine"/> so the narrowing agrees exactly with the filtering the same
    /// conditions get afterwards — case-insensitive <c>LIKE</c> included.
    /// </summary>
    private static IReadOnlyList<TeamCityBuildType> Restrict(IReadOnlyList<TeamCityBuildType> catalog, QueryPushdown pushdown)
    {
        if (pushdown.LocalConditions.Count == 0 || catalog.Count == 0)
        {
            return catalog;
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(catalog.Count);
        for (var index = 0; index < catalog.Count; index++)
        {
            var row = TeamCityRows.BuildType(catalog[index], 0, 0);
            row[IndexKey] = index;
            rows.Add(row);
        }

        var narrowing = new QueryRequest
        {
            Filter = new FilterGroup
            {
                Type = "group",
                Operator = LogicalOperator.And,
                Items = pushdown.LocalConditions
            }
        };

        // The index is not in BuildTypeQueryFields, so it cannot be filtered on — it only rides along.
        var matched = new InMemoryQueryEngine(BuildTypeQueryFields.All).Execute(rows, narrowing);

        return
        [
            .. matched
                .Select(row => row.TryGetValue(IndexKey, out var index) && index is int position ? catalog[position] : null)
                .OfType<TeamCityBuildType>()
        ];
    }
}
