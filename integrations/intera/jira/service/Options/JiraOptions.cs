namespace Intera.Jira.Api.Options;

public sealed class JiraOptions
{
    public const string SectionName = "Jira";

    /// <summary>The gateway every data read goes through: <c>{ApiGatewayUrl}/ex/jira/{cloudId}/rest/…</c>.</summary>
    public string ApiGatewayUrl { get; set; } = "https://api.atlassian.com";

    /// <summary>Lists the sites the stored grant covers (cloud id, URL, name, scopes).</summary>
    public string AccessibleResourcesUrl { get; set; } = "https://api.atlassian.com/oauth/token/accessible-resources";

    public string AuthorizeUrl { get; set; } = "https://auth.atlassian.com/authorize";

    public string TokenUrl { get; set; } = "https://auth.atlassian.com/oauth/token";

    /// <summary>The OAuth 2.0 (3LO) app's client id (developer.atlassian.com → Developer console → the app → Settings).</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Explicit callback URL override (e.g. <c>https://jira.example.com/oauth/callback</c> or
    /// <c>http://localhost:8110/oauth/callback</c>). Leave empty to relay OAuth redirects automatically through
    /// INTERA Core's webhook address (<c>PUT /oauth/redirect-uri</c>, persisted in <see cref="RedirectUriFilePath"/>).
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Path (relative to the app base directory unless rooted) of the file storing the redirect URL registered by INTERA.
    /// </summary>
    public string RedirectUriFilePath { get; set; } = "App_Data/jira-redirect-uri.json";

    /// <summary>
    /// Space- or comma-separated OAuth scopes.
    /// The platform REST API is read with the classic <c>read:jira-work</c> / <c>read:jira-user</c>; the Agile
    /// (Jira Software) API offers granular scopes only, so boards and sprints need the <c>*:jira-software</c> ones,
    /// and custom field options need the two granular field scopes (plus a Jira administrator as the consenting
    /// user). Read-only throughout: this service never writes to Jira.
    /// </summary>
    public string Scopes { get; set; } =
        "offline_access read:jira-work read:jira-user " +
        "read:board-scope:jira-software read:board-scope.admin:jira-software read:sprint:jira-software read:project:jira " +
        "read:field:jira read:field.option:jira read:custom-field-contextual-configuration:jira";

    /// <summary>
    /// The Jira site to read when the caller names none (<c>X-Jira-Site</c>), as its URL
    /// (<c>https://acme.atlassian.net</c>) or cloud id. Empty is fine while the grant covers exactly one site; with
    /// several, a request naming none is refused with the list.
    /// </summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>
    /// A JQL restriction every issue read of this deployment is ANDed with (e.g. <c>project in (DEV, OPS)</c>).
    /// An integration instance may narrow it further with its own scope (<c>X-Jira-Jql-Scope</c>), never widen it.
    /// Empty: no deployment-wide restriction.
    /// </summary>
    public string JqlScope { get; set; } = string.Empty;

    /// <summary>
    /// A read that would return more issues than this is refused as too broad rather than truncated. With the JQL
    /// scopes, this is what bounds an issue read: there is no implicit date window.
    /// </summary>
    public int MaxIssuesPerRead { get; set; } = 20_000;

    /// <summary>
    /// A list read (users, versions, worklogs of one issue, …) longer than this is refused rather than truncated. Higher
    /// than <see cref="MaxIssuesPerRead"/> because a service-management site's user directory holds every customer.
    /// </summary>
    public int MaxRecordsPerList { get; set; } = 200_000;

    /// <summary>
    /// Issues per enhanced-search page. Jira returns up to 5000 for ids only and fewer as more fields are asked for,
    /// whatever is requested; the token paging is followed either way.
    /// </summary>
    public int SearchPageSize { get; set; } = 100;

    /// <summary>
    /// The custom fields <c>jira_issues</c> carries when a query asks for rows as they are (no expressions), and
    /// that the plugin adds to its field list: ids (<c>customfield_10037</c>) or names, comma-separated, or <c>*</c>
    /// for every custom field the site has. Empty: none (including every custom field by default makes every
    /// issue page heavy). A query naming a custom field in its expressions, filter or order always gets it.
    /// </summary>
    public string IssueCustomFields { get; set; } = string.Empty;

    /// <summary>
    /// Worklogs of at most this many issues are read one issue at a time; more switch to the site-wide
    /// <c>/worklog/updated</c> + <c>/worklog/list</c> pair.
    /// </summary>
    public int WorklogPerIssueMaxIssues { get; set; } = 50;

    /// <summary>Issues one comments read may fan out to (one call each, at least); more is refused as too broad.</summary>
    public int MaxCommentIssues { get; set; } = 500;

    /// <summary>Projects, boards or custom fields one read may fan out to (versions, components, sprints, options).</summary>
    public int MaxFanout { get; set; } = 500;

    /// <summary>Path (relative to the app base directory unless rooted) of the file the Atlassian grant is kept in.</summary>
    public string OAuthTokenFilePath { get; set; } = "App_Data/jira-oauth-token.json";

    /// <summary>Key the token is stored under; a token file written for another key reads as "not authorised".</summary>
    public string ProviderName { get; set; } = "Jira";

    public int AccessTokenRefreshSkewSeconds { get; set; } = 120;

    /// <summary>
    /// A rotating refresh token Atlassian has not seen used for 90 days expires, and with it the connection. The
    /// service refreshes on its own once the stored token is this many days old, so an idle deployment stays
    /// connected. 0 turns that off.
    /// </summary>
    public int RefreshTokenKeepAliveDays { get; set; } = 7;

    /// <summary>Records per page on the offset-paged endpoints (projects cap this at 100 themselves).</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Concurrent calls to one site.</summary>
    public int MaxParallelRequests { get; set; } = 4;

    /// <summary>A 429 asking to wait longer than this is not waited out but answered as a 429.</summary>
    public int MaxRetryAfterSeconds { get; set; } = 60;

    public int HttpTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// How long an answer from Jira is reused. One dashboard asks for the same source several times at once (rows,
    /// a page, its total), and the issue sources share one search; 0 turns it off.
    /// </summary>
    public int CacheSeconds { get; set; } = 120;

    /// <summary>The same for reference data: projects, users, fields, statuses, priorities, issue types, boards.</summary>
    public int ReferenceCacheSeconds { get; set; } = 900;
}
