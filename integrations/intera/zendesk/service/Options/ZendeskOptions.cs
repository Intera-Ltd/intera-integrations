namespace Intera.Zendesk.Api.Options;

public sealed class ZendeskOptions
{
    public const string SectionName = "Zendesk";

    /// <summary>
    /// The account's subdomain: <c>acme</c> for <c>https://acme.zendesk.com</c>. Letters, digits and hyphens only —
    /// it becomes a host name that carries the API credential.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Overrides the whole base URL (a host-mapped account, or a stub in tests). Empty: <c>https://{Subdomain}.zendesk.com</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The OAuth client's identifier (Admin Center → Apps and integrations → APIs → OAuth clients → the client's
    /// <i>Unique identifier</i>, e.g. <c>intera</c>).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The OAuth client's secret (shown once, when the client is created). Confidential clients only.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The redirect URL, set by the deployment — e.g. <c>https://zendesk.example.com/oauth/callback</c> for a deployment
    /// with its own public host, or <c>http://localhost:8110/oauth/callback</c> for a trial. <b>Empty is the usual case
    /// from 4.0</b>: the plugin registers INTERA's own webhook address for the callback
    /// (<c>PUT /oauth/redirect-uri</c>, kept in <see cref="RedirectUriFilePath"/>), and Core relays Zendesk's redirect to
    /// <c>/oauth/callback</c> here. When set, this value wins and a registration that differs from it is refused. Either
    /// way it must equal, character for character, one of the OAuth client's registered redirect URLs.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Path (relative to the app base directory unless rooted) of the file the redirect URL INTERA registered is kept in.
    /// </summary>
    public string RedirectUriFilePath { get; set; } = "App_Data/zendesk-redirect-uri.json";

    /// <summary>
    /// Space- or comma-separated. <c>read</c> is every GET of the API and nothing that writes: this service never
    /// writes to Zendesk.
    /// </summary>
    public string Scopes { get; set; } = "read";

    /// <summary>Path (relative to the app base directory unless rooted) of the file the Zendesk grant is kept in.</summary>
    public string OAuthTokenFilePath { get; set; } = "App_Data/zendesk-oauth-token.json";

    /// <summary>Key the token is stored under; a token file written for another key reads as "not authorised".</summary>
    public string ProviderName { get; set; } = "Zendesk";

    public int AccessTokenRefreshSkewSeconds { get; set; } = 120;

    /// <summary>
    /// Zendesk's refresh tokens expire (the lifetime is the client's setting, 7 to 90 days) and rotate on every use, so a
    /// deployment nobody queries would lose its connection. The service refreshes on its own once the stored grant is
    /// this many days old. 0 turns that off. Keep it well under the shortest refresh-token lifetime you configure.
    /// </summary>
    public int RefreshTokenKeepAliveDays { get; set; } = 3;

    /// <summary>Fixed shared secret inbound callers must send as X-Access-Token / Bearer to reach this API.</summary>
    public string ApiAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Read tickets (and the metrics of tickets) through the Incremental Export API — cursor-based, 1000 a page, and
    /// narrowed by a lower bound on the ticket's time — instead of <c>GET /tickets</c> and the Search export. It is
    /// open to direct customer accounts but restricted for some third-party/Marketplace apps, hence off by default.
    /// </summary>
    public bool UseIncrementalExport { get; set; }

    /// <summary>
    /// The ticket custom fields <c>tickets</c> carries when a query asks for rows as they are, and that the plugin adds
    /// to its field list: field ids (<c>360012345678</c>) or titles, comma-separated, or <c>*</c> for every custom field.
    /// Empty: none. A query naming a <c>custom_field_NNN</c> column in its expressions, filter or order always gets it.
    /// </summary>
    public string TicketCustomFields { get; set; } = string.Empty;

    /// <summary>Records per page on cursor-paged lists (Zendesk caps it at 100; the Search export at 1000).</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>A read that would return more records than this is refused rather than truncated.</summary>
    public int MaxRecordsPerRead { get; set; } = 200_000;

    /// <summary>Tickets one comments / per-ticket read may fan out to (one call each); more is refused as too broad.</summary>
    public int MaxFanoutTickets { get; set; } = 500;

    /// <summary>Concurrent calls to the account.</summary>
    public int MaxParallelRequests { get; set; } = 4;

    /// <summary>A 429 asking to wait longer than this is not waited out but answered as a 429.</summary>
    public int MaxRetryAfterSeconds { get; set; } = 60;

    public int HttpTimeoutSeconds { get; set; } = 100;

    /// <summary>How long an answer from Zendesk is reused; 0 turns it off.</summary>
    public int CacheSeconds { get; set; } = 120;

    /// <summary>The same for reference data: users, organizations, groups, fields, forms, SLA policies.</summary>
    public int ReferenceCacheSeconds { get; set; } = 900;
}
