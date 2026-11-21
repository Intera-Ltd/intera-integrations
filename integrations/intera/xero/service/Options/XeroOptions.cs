namespace Intera.Xero.Api.Options;

public sealed class XeroOptions
{
    public const string SectionName = "Xero";

    /// <summary>The Accounting API root every data read goes to.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.xero.com/api.xro/2.0";

    /// <summary>Lists the organisations the stored grant covers (tenant id, name, connection id).</summary>
    public string ConnectionsUrl { get; set; } = "https://api.xero.com/connections";

    public string AuthorizeUrl { get; set; } = "https://login.xero.com/identity/connect/authorize";

    public string TokenUrl { get; set; } = "https://identity.xero.com/connect/token";

    /// <summary>The Xero app's client id (developer.xero.com → My Apps).</summary>
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Must equal, character for character, a redirect URI registered on the Xero app. Xero accepts plain HTTP for
    /// <c>localhost</c> only, so a deployment needs the public HTTPS name nginx serves — e.g.
    /// <c>https://xero.example.com/oauth/callback</c>.
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Space- or comma-separated OAuth scopes. Apps created after 2 March 2026 can only be granted Xero's granular scopes, which is why
    /// the default names them rather than the broad <c>accounting.transactions.read</c> /
    /// <c>accounting.reports.read</c>; an older app may override this with the broad pair until September 2027.
    /// Read-only throughout: this service never writes to Xero.
    /// </summary>
    public string Scopes { get; set; } =
        "openid profile email offline_access " +
        "accounting.settings.read accounting.contacts.read " +
        "accounting.invoices.read accounting.payments.read accounting.banktransactions.read accounting.manualjournals.read " +
        "accounting.budgets.read " +
        "accounting.reports.profitandloss.read accounting.reports.balancesheet.read accounting.reports.trialbalance.read " +
        "accounting.reports.banksummary.read accounting.reports.budgetsummary.read accounting.reports.aged.read";

    /// <summary>
    /// The organisation to read when the caller names none (<c>X-Xero-Tenant-Id</c>). Empty is fine while the grant
    /// covers exactly one organisation; with several, a request naming none is refused with the list.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Fixed shared secret inbound callers must send as X-Access-Token / Bearer to reach this API.</summary>
    public string ApiAccessToken { get; set; } = string.Empty;

    /// <summary>Path (relative to the app base directory unless rooted) of the file the Xero token is kept in.</summary>
    public string OAuthTokenFilePath { get; set; } = "App_Data/xero-oauth-token.json";

    /// <summary>Key the token is stored under; a token file written for another key reads as "not authorised".</summary>
    public string ProviderName { get; set; } = "Xero";

    public int AccessTokenRefreshSkewSeconds { get; set; } = 120;

    /// <summary>
    /// A refresh token Xero has not seen used for 60 days expires, and with it the whole connection. The service
    /// therefore refreshes on its own once the stored token is this many days old, so an idle deployment stays
    /// connected. 0 turns that off.
    /// </summary>
    public int RefreshTokenKeepAliveDays { get; set; } = 7;

    /// <summary>Records per page on the paged endpoints. Xero allows up to 1000 (the default without it is 100).</summary>
    public int PageSize { get; set; } = 1000;

    /// <summary>A read that would page past this many records is refused as too broad rather than truncated.</summary>
    public int MaxRowsPerRead { get; set; } = 100_000;

    /// <summary>Concurrent calls to one organisation. Xero allows five; one is left for anything else using the app.</summary>
    public int MaxParallelRequests { get; set; } = 4;

    /// <summary>A 429 asking to wait longer than this is not waited out but answered as a 429.</summary>
    public int MaxRetryAfterSeconds { get; set; } = 60;

    public int HttpTimeoutSeconds { get; set; } = 100;

    /// <summary>
    /// How long an answer from Xero is reused. One dashboard asks for the same source several times at once (rows,
    /// a page, its total), and the headers and the lines of a document come from the same read; against 60 calls a
    /// minute and 5000 a day per organisation that reuse is what keeps a dashboard inside Xero's limits. 0 turns it
    /// off.
    /// </summary>
    public int CacheSeconds { get; set; } = 60;

    /// <summary>The same for what other sources join against: the organisation, the chart of accounts, tracking.</summary>
    public int ReferenceCacheSeconds { get; set; } = 600;

    /// <summary>The longest window, in months, a monthly report (P&amp;L, balance sheet, budget, bank summary) is read for.</summary>
    public int MaxReportMonths { get; set; } = 36;

    /// <summary>Report calls one query may make; a window or a tracking fan-out needing more is refused as too broad.</summary>
    public int MaxReportCalls { get; set; } = 36;

    /// <summary>Contacts one aged receivables/payables query may name — one report call each.</summary>
    public int MaxAgedContacts { get; set; } = 25;

    /// <summary>Budgets <c>budget_lines</c> reads — one call each.</summary>
    public int MaxBudgets { get; set; } = 50;
}
