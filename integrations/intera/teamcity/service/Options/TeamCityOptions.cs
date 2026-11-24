namespace Intera.TeamCity.Api.Options;

public sealed class TeamCityOptions
{
    public const string SectionName = "TeamCity";

    /// <summary>
    /// Root URL of the TeamCity server, e.g. https://teamcity.example.com — including
    /// the context path when TeamCity is served under one (https://example.com/teamcity).
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// TeamCity personal access token sent as "Authorization: Bearer &lt;token&gt;".
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Project ids whose build configurations are in scope, subprojects included. Empty means every
    /// project the token can see.
    /// </summary>
    public string[] Projects { get; set; } = [];

    /// <summary>
    /// How many recent finished builds of a configuration are looked through for the newest one that
    /// reported tests.
    /// </summary>
    public int TestBuildLookback { get; set; } = 5;

    /// <summary>
    /// Most build configurations one test-results query may read tests for.
    /// </summary>
    public int MaxBuildTypes { get; set; } = 100;

    /// <summary>Most test occurrences read from one build.</summary>
    public int MaxTestsPerBuild { get; set; } = 10000;

    public int MaxParallelRequests { get; set; } = 4;

    public int HttpTimeoutSeconds { get; set; } = 60;
}
