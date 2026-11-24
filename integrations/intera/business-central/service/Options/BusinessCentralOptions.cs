namespace Intera.BusinessCentral.Api.Options;

public sealed class BusinessCentralOptions
{
    public const string SectionName = "BusinessCentral";

    /// <summary>Microsoft Entra ID tenant id (GUID) the Business Central tenant lives under.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Business Central environment name within the tenant, e.g. "Production" or "Sandbox".</summary>
    public string Environment { get; set; } = "Production";

    /// <summary>
    /// Base URL for the BC API v2.0 group, e.g. https://api.businesscentral.dynamics.com/v2.0/{tenant}/{environment}/api/v2.0.
    /// Left empty to derive it from TenantId + Environment; set explicitly only for non-standard clouds (GCC, etc.).
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Microsoft Entra ID token endpoint, e.g. https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token.
    /// Left empty to derive it from TenantId.
    /// </summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    public string OAuthEndpoint
    {
        get => TokenEndpoint;
        set => TokenEndpoint = value;
    }

    /// <summary>Microsoft Entra ID app registration (client) id used for the client-credentials grant.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Microsoft Entra ID app registration client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>OAuth scope requested for the client-credentials grant.</summary>
    public string Scope { get; set; } = "https://api.businesscentral.dynamics.com/.default";

    /// <summary>How many seconds before actual expiry a cached access token is treated as needing refresh.</summary>
    public int AccessTokenRefreshSkewSeconds { get; set; } = 120;

    /// <summary>
    /// Hard cap on how many records are read per entity per request. BC OData collections page via
    /// @odata.nextLink with no server-side row cap of their own, so the client stops following nextLink
    /// once this is hit. Each collection is requested newest-first (lastModifiedDateTime desc, or
    /// entryNumber desc for GL entries) so the cap predictably drops the oldest records.
    /// </summary>
    public int MaxRecordsPerEntity { get; set; } = 5000;

    public int HttpTimeoutSeconds { get; set; } = 60;
}
