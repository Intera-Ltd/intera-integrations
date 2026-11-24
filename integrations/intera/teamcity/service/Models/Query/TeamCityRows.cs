using Intera.TeamCity.Api.Infrastructure;

namespace Intera.TeamCity.Api.Models.Query;

/// <summary>
/// Writes the row keys of both sources. <see cref="TeamCityQueryFields"/> is the allowlist over the same
/// names, and the plugin's descriptor lists are the third copy — a name in one and not the others is
/// either a column nobody can filter on or one that silently reads null.
/// </summary>
public static class TeamCityRows
{
    /// <summary>
    /// The columns a test row repeats from its build configuration. They carry the same names on both
    /// sources, which is what lets a test-results filter on them be evaluated against the configuration
    /// list before any test is read.
    /// </summary>
    public static readonly string[] BuildTypeColumns =
    [
        "project_id",
        "project_name",
        "build_type_id",
        "build_type_name",
        "build_type_web_url",
        "paused"
    ];

    public static Dictionary<string, object?> BuildType(TeamCityBuildType buildType, int runningBuilds, int queuedBuilds)
    {
        ArgumentNullException.ThrowIfNull(buildType);

        var row = BuildTypeIdentity(buildType);
        var last = buildType.Builds?.Builds.FirstOrDefault();

        row["last_build_id"] = last?.Id;
        row["last_build_number"] = last?.Number;
        row["last_build_status"] = last?.Status;
        row["last_build_success"] = last is null ? null : IsSuccess(last.Status);
        row["last_build_status_text"] = last?.StatusText;
        row["last_build_branch"] = last?.BranchName;
        row["last_build_start_date"] = TeamCityTimestamp.Parse(last?.StartDate);
        row["last_build_finish_date"] = TeamCityTimestamp.Parse(last?.FinishDate);
        row["last_build_web_url"] = last?.WebUrl;

        // A build that reported no tests carries no counters at all, and zero is the true answer for it.
        // Only a configuration that has never finished a build reads null.
        var tests = last?.TestOccurrences;
        row["last_build_tests_total"] = last is null ? null : tests?.Count ?? 0;
        row["last_build_tests_passed"] = last is null ? null : tests?.Passed ?? 0;
        row["last_build_tests_failed"] = last is null ? null : tests?.Failed ?? 0;
        row["last_build_tests_ignored"] = last is null ? null : tests?.Ignored ?? 0;
        row["last_build_tests_muted"] = last is null ? null : tests?.Muted ?? 0;
        row["last_build_tests_new_failed"] = last is null ? null : tests?.NewFailed ?? 0;

        row["running_builds"] = runningBuilds;
        row["queued_builds"] = queuedBuilds;

        return row;
    }

    public static Dictionary<string, object?> TestResult(
        TeamCityBuildType buildType,
        TeamCityBuild build,
        bool fromLatestBuild,
        TeamCityTestOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(buildType);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(occurrence);

        var row = BuildTypeIdentity(buildType);
        var parsed = occurrence.Test?.ParsedTestName;

        row["build_id"] = build.Id;
        row["build_number"] = build.Number;
        row["build_status"] = build.Status;
        row["build_status_text"] = build.StatusText;
        row["build_branch"] = build.BranchName;
        row["build_start_date"] = TeamCityTimestamp.Parse(build.StartDate);
        row["build_finish_date"] = TeamCityTimestamp.Parse(build.FinishDate);
        row["build_web_url"] = build.WebUrl;
        row["from_latest_build"] = fromLatestBuild;

        row["test_id"] = occurrence.Test?.Id;
        row["test_name"] = occurrence.Test?.Name ?? occurrence.Name;
        row["test_package"] = Blank(parsed?.TestPackage);
        row["test_suite"] = Blank(parsed?.TestSuite);
        row["test_class"] = Blank(parsed?.TestClass);
        row["test_method"] = Blank(parsed?.TestMethodName);
        row["status"] = occurrence.Status;
        row["passed"] = IsSuccess(occurrence.Status);
        row["ignored"] = occurrence.Ignored == true;
        row["muted"] = occurrence.Muted == true;
        row["currently_muted"] = occurrence.CurrentlyMuted == true;
        row["currently_investigated"] = occurrence.CurrentlyInvestigated == true;
        row["new_failure"] = occurrence.NewFailure == true;
        row["duration_ms"] = occurrence.Duration;
        row["details"] = Blank(occurrence.Details);
        row["ignore_details"] = Blank(occurrence.IgnoreDetails);

        return row;
    }

    private static Dictionary<string, object?> BuildTypeIdentity(TeamCityBuildType buildType)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["project_id"] = buildType.ProjectId,
            ["project_name"] = buildType.ProjectName,
            ["build_type_id"] = buildType.Id,
            ["build_type_name"] = buildType.Name,
            ["build_type_web_url"] = buildType.WebUrl,
            ["paused"] = buildType.Paused == true
        };
    }

    private static bool IsSuccess(string? status) =>
        string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
