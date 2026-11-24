namespace Intera.Unleashed.Api.Options;

/// <summary>
/// Per-deployment configuration for the Unleashed API wrapper.
/// </summary>
public sealed class UnleashedOptions
{
    public const string SectionName = "Unleashed";

    /// <summary>Base URL of the Unleashed API.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.unleashedsoftware.com";

    /// <summary>
    /// The account's API id, sent as the <c>api-auth-id</c> header.
    /// </summary>
    public string ApiId { get; set; } = string.Empty;

    /// <summary>
    /// The account's API key. Used only as the HMAC-SHA256 secret over each request's query string;
    /// it never appears in a request.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The <c>client-type</c> header Unleashed requires, formatted as
    /// <c>&lt;partner_name&gt;/&lt;app_name&gt;</c>.
    /// </summary>
    public string ClientType { get; set; } = "intera/unleashed";

    /// <summary>
    /// Records per upstream page. Unleashed's own default is 200 and its maximum is 1000.
    /// </summary>
    public int PageSize { get; set; } = 200;

    /// <summary>
    /// Cap on how many pages a single upstream resource is read for one request.
    /// </summary>
    public int MaxPages { get; set; } = 20;

    /// <summary>
    /// Cap on how many warehouses a stock-on-hand request may fan out over.
    /// </summary>
    public int MaxWarehouses { get; set; } = 50;

    /// <summary>Concurrent upstream requests during a stock-on-hand fan-out.</summary>
    public int MaxParallelRequests { get; set; } = 4;

    /// <summary>
    /// How long the warehouse list is cached in seconds.
    /// </summary>
    public int CatalogCacheSeconds { get; set; } = 300;

    public int HttpTimeoutSeconds { get; set; } = 100;
}
