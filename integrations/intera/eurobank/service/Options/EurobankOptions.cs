namespace Intera.Eurobank.Api.Options;

public sealed class EurobankOptions
{
    public const string SectionName = "Eurobank";

    /// <summary>
    /// Base URL for the resource (account/payment) endpoints, e.g. <c>{ApiBaseUrl}/accounts</c> — the services server
    /// per the official OpenAPI specification, distinct from <see cref="AuthBaseUrl"/>. Defaults to the B2B role
    /// (<c>/v2/b2b</c>), which does not require a PSD2 TPP license; mTLS is mandatory in production and optional in
    /// sandbox.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://apis.hellenicbank.com/v2/b2b";

    /// <summary>
    /// Base URL for the OAuth 2.0 endpoints (<c>/v2/oauth2/auth</c>, <c>/v2/token/exchange</c>, <c>/v2/token</c>).
    /// mTLS is not required against this host; client authentication uses HTTP Basic (<c>client_id:client_secret</c>).
    /// </summary>
    public string AuthBaseUrl { get; set; } = "https://oauth.hellenicbank.com";

    /// <summary>Developer Portal base URL for reference links.</summary>
    public string DeveloperPortalBaseUrl { get; set; } = "https://openbanking.hellenicbank.com";

    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Issued alongside <see cref="ClientId"/> when the application is created in the Developer Portal and sent via
    /// HTTP Basic authentication on <c>/v2/token/exchange</c> and <c>/v2/token</c>.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Comma-separated Eurobank v2 OAuth scopes requested at <c>/v2/oauth2/auth</c>.</summary>
    public string Scope { get; set; } = "v2.b2b.get.accounts,v2.b2b.get.account.details,v2.b2b.get.account.transactions";

    /// <summary>PEM-encoded mTLS client certificate issued by the bank (mandatory in production, optional in sandbox).</summary>
    public string ClientCertificatePem { get; set; } = string.Empty;

    /// <summary>PEM-encoded private key matching <see cref="ClientCertificatePem"/>.</summary>
    public string ClientPrivateKeyPem { get; set; } = string.Empty;

    /// <summary>Alternative compact Base64-encoded PKCS#12 (.pfx) bundle when under 4096 characters.</summary>
    public string ClientCertificatePfxBase64 { get; set; } = string.Empty;

    /// <summary>Optional local filesystem path fallback to a PKCS#12 (.pfx) bundle for local development.</summary>
    public string ClientCertificatePath { get; set; } = string.Empty;

    public string ClientCertificatePassword { get; set; } = string.Empty;

    public string ProviderName { get; set; } = "EurobankB2B";

    public int AccessTokenRefreshSkewSeconds { get; set; } = 120;

    public int HttpTimeoutSeconds { get; set; } = 100;

    /// <summary>Path (relative to the app base directory unless rooted) of the encrypted file persisting Eurobank's OAuth grant.</summary>
    public string OAuthTokenFilePath { get; set; } = "App_Data/eurobank-oauth-token.json";

    /// <summary>Path (relative to the app base directory unless rooted) of the JSON file persisting INTERA's registered webhook redirect URI.</summary>
    public string RedirectUriFilePath { get; set; } = "App_Data/eurobank-redirect-uri.json";
}
