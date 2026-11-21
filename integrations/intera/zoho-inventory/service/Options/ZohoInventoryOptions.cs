namespace Intera.ZohoInventory.Api.Options;

/// <summary>
/// Per-deployment configuration for the Zoho Inventory API wrapper.
/// </summary>
/// <remarks>
/// <para>
/// The credential is a Zoho <b>Self Client</b>: a client id and secret plus a refresh token minted
/// once, by hand, in the Zoho API console. The refresh token never expires on its own, so the
/// service needs no browser flow and no <c>/oauth/login</c> — it trades the refresh token for a
/// one-hour access token whenever it needs one. All of it belongs to one Zoho organization, so it
/// lives here rather than in the integration instance.
/// </para>
/// <para>
/// Zoho keeps every data center apart — an EU account does not exist on the US hosts at all — and
/// the accounts host and the API host have to agree. <see cref="DataCenter"/> names the pair once;
/// the two URL settings exist only as overrides.
/// </para>
/// </remarks>
public sealed class ZohoInventoryOptions
{
    public const string SectionName = "ZohoInventory";

    /// <summary>
    /// The Zoho data center the organization lives in: <c>com</c>, <c>eu</c>, <c>in</c>,
    /// <c>com.au</c>, <c>jp</c>, <c>ca</c>, <c>com.cn</c> or <c>sa</c> — the domain suffix of the
    /// organization's own web app URL (<c>inventory.zoho.eu</c> → <c>eu</c>).
    /// </summary>
    public string DataCenter { get; set; } = "com";

    /// <summary>
    /// Overrides the accounts host derived from <see cref="DataCenter"/>, e.g.
    /// <c>https://accounts.zoho.eu</c>. Normally empty.
    /// </summary>
    public string AccountsUrl { get; set; } = string.Empty;

    /// <summary>
    /// Overrides the API root, e.g. <c>https://www.zohoapis.eu/inventory/v1</c>. Normally empty:
    /// the host then comes from the token response's own <c>api_domain</c>, which is the value Zoho
    /// says to use.
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The organization every request is scoped to. Zoho requires it on every call, and a Zoho user
    /// can belong to several organizations, so it is never inferred.
    /// </summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>Client ID of the Self Client in the Zoho API console.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client Secret of the same Self Client.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// The refresh token minted from a Self Client grant code. It acts as the Zoho user who
    /// generated the code, within that user's role and the scopes it was issued for.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>Fixed shared secret inbound callers must send as X-Access-Token / Bearer.</summary>
    public string ApiAccessToken { get; set; } = string.Empty;

    /// <summary>Records per list page. Zoho's default and maximum are both 200.</summary>
    public int PageSize { get; set; } = 200;

    /// <summary>
    /// Cap on how many pages one list is read for. Reaching it with more pages left is refused rather
    /// than truncated — see <c>ZohoQueryTooBroadException</c>.
    /// </summary>
    public int MaxListPages { get; set; } = 25;

    /// <summary>
    /// Cap on how many documents one query may read in detail. A line-grained source costs one Zoho
    /// call per document, against a daily allowance of a few thousand calls, so a query over more
    /// documents than this is refused with a message asking to narrow it.
    /// </summary>
    public int MaxDocumentsPerQuery { get; set; } = 200;

    /// <summary>
    /// Items per <c>/itemdetails</c> call when stock is read per location. The ids travel comma-joined
    /// in the query string, and Zoho documents no maximum.
    /// </summary>
    public int ItemDetailsBatchSize { get; set; } = 100;

    /// <summary>Concurrent upstream requests during a document fan-out. Zoho allows 10 on paid plans.</summary>
    public int MaxParallelRequests { get; set; } = 4;

    /// <summary>
    /// Upstream requests allowed per minute, process-wide. Zoho's own limit is 100 per organization
    /// and, unlike a daily quota, exceeding it gets the <b>whole organization</b> temporarily blocked
    /// — every other integration and the web app's own API use included. The margin below 100 is for
    /// whatever else calls the same organization.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 80;

    /// <summary>How long before its stated expiry an access token is replaced.</summary>
    public int AccessTokenRefreshSkewSeconds { get; set; } = 300;

    public int HttpTimeoutSeconds { get; set; } = 100;
}
