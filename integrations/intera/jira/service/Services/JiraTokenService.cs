using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Intera.Jira.Api.Infrastructure;
using Intera.Jira.Api.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Jira.Api.Services;

public sealed record JiraOAuthStatus(
    bool Configured,
    bool HasAccessToken,
    bool HasRefreshToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? UpdatedAt,
    string? Scope,
    string RedirectUri,
    string? RedirectUriSource);

/// <summary>
/// The deployment's one Atlassian grant: the OAuth 2.0 (3LO) authorization-code flow that creates it, and the
/// refresh that keeps it. The same shape as <c>XeroTokenService</c>, which it was copied from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atlassian rotates the refresh token on every use</b> (with <c>offline_access</c>): each refresh answers with a
/// new one and the old one stops working after a 10-minute reuse interval. That is why the token lives in this
/// service's own file (<c>Jira:OAuthTokenFilePath</c>) and not in the integration instance's configuration, and why
/// refreshes are serialised behind one gate. A refresh token unused for 90 days expires — see
/// <see cref="JiraTokenKeepAliveService"/>.
/// </para>
/// <para>
/// <b>The authorization is started by an authenticated call</b> (<c>GET /oauth/authorize-url</c>), and the callback
/// accepts only a <c>state</c> this service issued, once, within fifteen minutes — whoever completes the flow decides
/// which Jira site, as which user, the deployment reads.
/// </para>
/// <para>
/// <b>The redirect URL is INTERA's own webhook address by default</b>: the plugin registers it through
/// <see cref="RegisterRedirectUriAsync"/> when its instance is saved, Atlassian redirects the browser to Core, and Core
/// relays the callback to <c>/oauth/callback</c> here. Each issued state keeps the exact redirect URL its sign-in
/// link was built with so the code exchange always sends the matching <c>redirect_uri</c>.
/// </para>
/// <para>
/// Atlassian's token endpoint takes a JSON body carrying the client id and secret (not HTTP Basic, unlike Xero's).
/// A Singleton, holding the refresh gate and the issued states; the HTTP client comes from the factory per call.
/// </para>
/// </remarks>
public sealed class JiraTokenService(
    IOptionsMonitor<JiraOptions> options,
    IOAuthTokenStore tokenStore,
    IOAuthRedirectUriStore redirectUriStore,
    IHttpClientFactory httpClientFactory,
    ILogger<JiraTokenService> logger)
{
    public const string HttpClientName = "jira";

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IssuedState> issuedStates = new(StringComparer.Ordinal);
    private int grantVersion;

    private JiraOptions Options => options.CurrentValue;

    /// <summary>Changes whenever a new grant is stored, so what was cached for the old one can be dropped.</summary>
    public int GrantVersion => Volatile.Read(ref grantVersion);

    public async Task<string> BuildAuthorizationUrlAsync(CancellationToken cancellationToken)
    {
        RequireClient();
        var authorizeUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.AuthorizeUrl, "Jira:AuthorizeUrl");

        var redirectUri = (await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false)).RedirectUri
            ?? throw new InvalidOperationException(
                "There is no redirect URL yet. Save the Jira connection in INTERA so Core registers its callback address "
                + "(or set Jira:RedirectUri for a deployment with its own public host).");

        var now = DateTimeOffset.UtcNow;
        foreach (var (issued, entry) in issuedStates)
        {
            if (entry.ExpiresAt <= now)
            {
                issuedStates.TryRemove(issued, out _);
            }
        }

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        issuedStates[state] = new IssuedState(now + StateLifetime, redirectUri);

        var query = string.Join(
            "&",
            "audience=api.atlassian.com",
            $"client_id={Uri.EscapeDataString(Options.ClientId)}",
            $"scope={Uri.EscapeDataString(NormalizeScopes(Options.Scopes))}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"state={Uri.EscapeDataString(state)}",
            "response_type=code",
            "prompt=consent");

        return $"{authorizeUri.AbsoluteUri.TrimEnd('?')}?{query}";
    }

    public async Task<OAuthRedirectUriState> GetRedirectUriAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Options.RedirectUri))
        {
            return new OAuthRedirectUriState(Options.RedirectUri.Trim(), OAuthRedirectUriState.ConfigurationSource);
        }

        var registered = await redirectUriStore.GetAsync(cancellationToken).ConfigureAwait(false);
        return registered is null
            ? new OAuthRedirectUriState(null, null)
            : new OAuthRedirectUriState(registered, OAuthRedirectUriState.InteraSource);
    }

    public async Task<OAuthRedirectUriState> RegisterRedirectUriAsync(string? redirectUri, CancellationToken cancellationToken)
    {
        var validated = OAuthRedirectUriValidator.Validate(redirectUri);

        if (!string.IsNullOrWhiteSpace(Options.RedirectUri))
        {
            var configured = Options.RedirectUri.Trim();
            if (!string.Equals(configured, validated, StringComparison.Ordinal))
            {
                throw new OAuthRedirectUriConflictException(
                    $"This deployment configures its own redirect URL ({configured}), so INTERA's callback address ({validated}) is not used. "
                    + "Clear JIRA__REDIRECTURI and redeploy to let INTERA relay the sign-in.");
            }

            return new OAuthRedirectUriState(configured, OAuthRedirectUriState.ConfigurationSource);
        }

        await redirectUriStore.SaveAsync(validated, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Jira redirect URL registered by INTERA: {RedirectUri}.", validated);

        return new OAuthRedirectUriState(validated, OAuthRedirectUriState.InteraSource);
    }

    public async Task ExchangeAuthorizationCodeAsync(string code, string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new JiraRequestException("The authorization code is missing.");
        }

        if (string.IsNullOrWhiteSpace(state)
            || !issuedStates.TryRemove(state, out var issued)
            || issued.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new JiraRequestException(
                "The authorization state is unknown or expired. Test connection on the Jira instance in INTERA (or call GET /oauth/authorize-url) — a link is valid once, for 15 minutes.");
        }

        RequireClient();

        var response = await RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = issued.RedirectUri
            },
            cancellationToken).ConfigureAwait(false);

        await SaveAsync(response, previousRefreshToken: null, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref grantVersion);

        logger.LogInformation("Atlassian grant stored (scopes: {Scope}).", response.Scope);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false)
            ?? throw NotAuthorised();

        if (!NeedsRefresh(token))
        {
            return token.AccessToken!;
        }

        token = await RefreshUnderGateAsync(token.AccessToken, force: false, cancellationToken).ConfigureAwait(false);
        return token.AccessToken!;
    }

    /// <summary>
    /// Atlassian refused <paramref name="rejectedAccessToken"/> before its stated expiry. Refreshes once — unless
    /// another request already did, in which case the newer token is simply returned.
    /// </summary>
    public async Task<string> RefreshAfterRejectionAsync(string rejectedAccessToken, CancellationToken cancellationToken)
    {
        var token = await RefreshUnderGateAsync(rejectedAccessToken, force: true, cancellationToken).ConfigureAwait(false);
        return token.AccessToken!;
    }

    /// <summary>Refreshes now, whatever the expiry says — the diagnostic <c>POST /oauth/refresh</c> and the keep-alive.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken) =>
        RefreshUnderGateAsync(rejectedAccessToken: null, force: true, cancellationToken);

    public async Task<JiraOAuthStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false);
        var redirect = await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false);

        return new JiraOAuthStatus(
            !string.IsNullOrWhiteSpace(Options.ClientId) && !string.IsNullOrWhiteSpace(Options.ClientSecret),
            !string.IsNullOrWhiteSpace(token?.AccessToken),
            !string.IsNullOrWhiteSpace(token?.RefreshToken),
            token?.AccessTokenExpiresAt,
            token?.UpdatedAt,
            token?.Scope,
            redirect.RedirectUri ?? string.Empty,
            redirect.Source);
    }

    /// <summary>When the stored grant was last refreshed or created; null when there is none.</summary>
    public async Task<DateTimeOffset?> GetLastRefreshAsync(CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(token?.RefreshToken) ? null : token.UpdatedAt;
    }

    private async Task<OAuthToken> RefreshUnderGateAsync(string? rejectedAccessToken, bool force, CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false)
                ?? throw NotAuthorised();

            // Another request refreshed while this one waited at the gate: its token is the answer.
            var alreadyReplaced = rejectedAccessToken is not null
                && !string.Equals(token.AccessToken, rejectedAccessToken, StringComparison.Ordinal);
            if (alreadyReplaced && !NeedsRefresh(token))
            {
                return token;
            }

            if (!force && !NeedsRefresh(token))
            {
                return token;
            }

            if (string.IsNullOrWhiteSpace(token.RefreshToken))
            {
                throw NotAuthorised();
            }

            RequireClient();

            var response = await RequestTokenAsync(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = Options.ClientId,
                    ["client_secret"] = Options.ClientSecret,
                    ["refresh_token"] = token.RefreshToken
                },
                cancellationToken).ConfigureAwait(false);

            return await SaveAsync(response, token.RefreshToken, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task<AtlassianTokenResponse> RequestTokenAsync(Dictionary<string, string> body, CancellationToken cancellationToken)
    {
        var tokenUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.TokenUrl, "Jira:TokenUrl");

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUri)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClientFactory.CreateClient(HttpClientName)
            .SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        AtlassianTokenResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<AtlassianTokenResponse>(text);
        }
        catch (JsonException)
        {
            // Not JSON — reported below with the status.
        }

        if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(parsed?.AccessToken))
        {
            return parsed;
        }

        var error = parsed?.Error;
        var detail = string.IsNullOrWhiteSpace(parsed?.ErrorDescription) ? error : $"{error}: {parsed.ErrorDescription}";

        throw error switch
        {
            // The refresh token is spent (rotated past its reuse interval), expired (90 days unused) or revoked, or
            // the code was used already. Only a new authorization helps.
            "invalid_grant" or "access_denied" => new JiraAuthorizationException(
                $"Atlassian refused the stored grant ({detail}). Authorise this deployment again: GET /oauth/authorize-url."),
            "invalid_client" or "unauthorized_client" => new JiraAuthorizationException(
                $"Atlassian refused the client credentials ({detail}) — check Jira:ClientId and Jira:ClientSecret."),
            _ when response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new JiraAuthorizationException($"Atlassian's token endpoint answered {(int)response.StatusCode}: {detail ?? Truncate(text)}"),
            _ => new JiraUpstreamException($"Atlassian's token endpoint answered {(int)response.StatusCode}: {detail ?? Truncate(text)}")
        };
    }

    private async Task<OAuthToken> SaveAsync(AtlassianTokenResponse response, string? previousRefreshToken, CancellationToken cancellationToken)
    {
        var token = new OAuthToken(
            Options.ProviderName,
            response.AccessToken,
            // A rotating grant always answers a refresh with a new refresh token; keeping the old one is only a
            // fallback for an answer that carries none, which would otherwise leave nothing to refresh with.
            string.IsNullOrWhiteSpace(response.RefreshToken) ? previousRefreshToken : response.RefreshToken,
            response.TokenType,
            response.Scope,
            response.ExpiresIn is { } seconds ? DateTimeOffset.UtcNow.AddSeconds(seconds) : null,
            DateTimeOffset.UtcNow);

        await tokenStore.SaveAsync(token, cancellationToken).ConfigureAwait(false);
        return token;
    }

    private bool NeedsRefresh(OAuthToken token) =>
        string.IsNullOrWhiteSpace(token.AccessToken)
        || (token.AccessTokenExpiresAt is { } expiresAt
            && expiresAt <= DateTimeOffset.UtcNow.AddSeconds(Options.AccessTokenRefreshSkewSeconds));

    private void RequireClient()
    {
        if (string.IsNullOrWhiteSpace(Options.ClientId) || string.IsNullOrWhiteSpace(Options.ClientSecret))
        {
            throw new InvalidOperationException("Jira:ClientId and Jira:ClientSecret are not configured.");
        }
    }

    private static JiraAuthorizationException NotAuthorised() =>
        new("This deployment is not connected to Jira yet. Test connection on the Jira instance in INTERA gives a sign-in link (so does GET /oauth/authorize-url); open it and sign in to Atlassian.");

    private static string NormalizeScopes(string scopes) =>
        string.Join(' ', scopes.Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string text) => text.Length <= 300 ? text : $"{text[..300]}…";

    private sealed record IssuedState(DateTimeOffset ExpiresAt, string RedirectUri);

    private sealed class AtlassianTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }
    }
}
