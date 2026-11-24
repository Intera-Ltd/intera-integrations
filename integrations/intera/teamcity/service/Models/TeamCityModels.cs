using System.Text.Json.Serialization;

namespace Intera.TeamCity.Api.Models;

// TeamCity's JSON is camelCase, so the Web defaults of JsonHttpClient bind these without attributes.
// The attributes are here anyway where a C# name would otherwise differ from the key, and on the
// collection properties, whose keys are singular ("buildType", "build") while holding a list.

public sealed class TeamCityBuildTypeList
{
    [JsonPropertyName("buildType")]
    public List<TeamCityBuildType> BuildTypes { get; set; } = [];

    public string? NextHref { get; set; }
}

public sealed class TeamCityBuildType
{
    public string Id { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? ProjectId { get; set; }

    /// <summary>The full project path as TeamCity prints it: "Parent / Child / Project".</summary>
    public string? ProjectName { get; set; }

    public string? WebUrl { get; set; }

    public bool? Paused { get; set; }

    /// <summary>The recent finished builds, newest first, as the nested <c>builds($locator(...))</c> asked.</summary>
    public TeamCityBuildList? Builds { get; set; }
}

public sealed class TeamCityBuildList
{
    [JsonPropertyName("build")]
    public List<TeamCityBuild> Builds { get; set; } = [];

    public string? NextHref { get; set; }
}

public sealed class TeamCityBuild
{
    public long Id { get; set; }

    public string? BuildTypeId { get; set; }

    public string? Number { get; set; }

    /// <summary>SUCCESS, FAILURE, ERROR or UNKNOWN.</summary>
    public string? Status { get; set; }

    public string? State { get; set; }

    public string? StatusText { get; set; }

    /// <summary>Absent on a configuration with no branch specification.</summary>
    public string? BranchName { get; set; }

    /// <summary>TeamCity's compact timestamp, e.g. 20260915T071146+0000.</summary>
    public string? StartDate { get; set; }

    public string? FinishDate { get; set; }

    public string? WebUrl { get; set; }

    /// <summary>Counters of the build's tests. Absent altogether on a build that reported none.</summary>
    public TeamCityTestCounters? TestOccurrences { get; set; }
}

public sealed class TeamCityTestCounters
{
    public int? Count { get; set; }

    public int? Passed { get; set; }

    public int? Failed { get; set; }

    public int? Ignored { get; set; }

    public int? Muted { get; set; }

    public int? NewFailed { get; set; }
}

public sealed class TeamCityTestOccurrenceList
{
    [JsonPropertyName("testOccurrence")]
    public List<TeamCityTestOccurrence> TestOccurrences { get; set; } = [];

    public string? NextHref { get; set; }
}

public sealed class TeamCityTestOccurrence
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>SUCCESS, FAILURE or UNKNOWN — an ignored test is UNKNOWN with <see cref="Ignored"/> set.</summary>
    public string? Status { get; set; }

    /// <summary>Milliseconds.</summary>
    public long? Duration { get; set; }

    public bool? Ignored { get; set; }

    public bool? Muted { get; set; }

    public bool? CurrentlyMuted { get; set; }

    public bool? CurrentlyInvestigated { get; set; }

    /// <summary>Only sent when true.</summary>
    public bool? NewFailure { get; set; }

    public string? Details { get; set; }

    public string? IgnoreDetails { get; set; }

    public TeamCityTest? Test { get; set; }
}

public sealed class TeamCityTest
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public TeamCityParsedTestName? ParsedTestName { get; set; }
}

public sealed class TeamCityParsedTestName
{
    public string? TestPackage { get; set; }

    public string? TestSuite { get; set; }

    public string? TestClass { get; set; }

    public string? TestShortName { get; set; }

    public string? TestMethodName { get; set; }
}
