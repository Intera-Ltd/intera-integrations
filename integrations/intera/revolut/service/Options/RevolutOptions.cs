namespace Intera.Revolut.Api.Options;

public sealed class RevolutOptions
{
    public const string SectionName = "Revolut";

    public string ApiBaseUrl { get; set; } = "https://b2b.revolut.com/api/1.0";

    public string BusinessPortalBaseUrl { get; set; } = "https://business.revolut.com";

    public string TokenEndpoint { get; set; } = "https://b2b.revolut.com/api/1.0/auth/token";

    public string OAuthEndpoint
    {
        get => TokenEndpoint;
        set => TokenEndpoint = value;
    }

    public string ClientId { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>PEM-encoded RSA private key whose public certificate is uploaded in Revolut Business API settings.</summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>Optional local filesystem path fallback for development when <see cref="PrivateKeyPem"/> is unset.</summary>
    public string PrivateKeyPath { get; set; } = string.Empty;

    public string ClientAssertionIssuer { get; set; } = string.Empty;

    public string ClientAssertionAudience { get; set; } = "https://revolut.com";

    public string ProviderName { get; set; } = "RevolutBusiness";

    public int AccessTokenRefreshSkewSeconds { get; set; } = 120;

    public int HttpTimeoutSeconds { get; set; } = 100;

    /// <summary>Path (relative to the app base directory unless rooted) of the encrypted file persisting Revolut's OAuth grant.</summary>
    public string OAuthTokenFilePath { get; set; } = "App_Data/revolut-oauth-token.json";

    /// <summary>Path (relative to the app base directory unless rooted) of the JSON file persisting INTERA's registered webhook redirect URI.</summary>
    public string RedirectUriFilePath { get; set; } = "App_Data/revolut-redirect-uri.json";

    /// <summary>
    /// How far back a transactions query reaches when its filter sets no lower bound on <c>created_at</c>, counted
    /// from the upper bound (or now). 90 days is also all Revolut serves on its Freelancer plans after the first
    /// five minutes of a grant.
    /// </summary>
    public int TransactionsLookbackDays { get; set; } = 90;
}
