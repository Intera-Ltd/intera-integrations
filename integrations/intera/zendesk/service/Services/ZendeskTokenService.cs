using Intera.Common.Infrastructure.OAuth;
using Intera.Zendesk.Api.Infrastructure;
using Intera.Zendesk.Api.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Zendesk.Api.Services;

/// <param name="RedirectUri">The redirect URL a sign-in would use now; empty when there is none yet.</param>
/// <param name="RedirectUriSource">
/// Where it comes from: <c>configuration</c> (<c>Zendesk:RedirectUri</c>), <c>intera</c> (registered by the plugin), or
/// null when there is none — then no sign-in can start until the instance in INTERA has registered its address.
/// </param>
public sealed record ZendeskOAuthStatus(
    bool Configured,
    bool HasAccessToken,
    bool HasRefreshToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? UpdatedAt,
    string? Scope,
    string RedirectUri,
    string? RedirectUriSource);

/// <summary>The redirect URL in effect and where it came from (see <see cref="ZendeskOAuthStatus.RedirectUriSource"/>).</summary>
public sealed record ZendeskRedirectUriState(string? RedirectUri, string? Source);

/// <summary>
/// The deployment's one Zendesk grant: the OAuth 2.0 authorization-code flow that creates it, and the refresh that
/// keeps it. The same shape as <c>JiraTokenService</c> and <c>XeroTokenService</c>, which it was copied from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why OAuth and not an API token:</b> Zendesk is removing API tokens — the Admin
/// Center announces that active ones stop working on 30 April 2027 — and recommends OAuth. A first version used a
/// static token and was dropped before it shipped.
/// </para>
/// <para>
/// <b>Zendesk's refresh tokens expire and rotate</b> (a new one comes with every refresh), so the grant lives in this
/// service's own file (<c>Zendesk:OAuthTokenFilePath</c>) and refreshes are serialised behind one gate. A deployment
/// nobody queries would lose it, hence <see cref="ZendeskTokenKeepAliveService"/>.
/// </para>
/// <para>
/// <b>The authorization is started by an authenticated call</b> (<c>GET /oauth/authorize-url</c>), and the callback
/// accepts only a <c>state</c> this service issued, once, within fifteen minutes — whoever completes the flow decides
/// as which Zendesk user the deployment reads. The authorize and token endpoints are on the account's own host
/// (<c>https://&lt;subdomain&gt;.zendesk.com/oauth/…</c>).
/// </para>
/// <para>
/// <b>The redirect URL is INTERA's own webhook address by default</b> (4.0): the plugin registers it through
/// <see cref="RegisterRedirectUriAsync"/> when its instance is saved, Zendesk sends the browser to Core, and Core relays
/// the request to the plugin's <c>onWebhook</c>, which forwards it to <c>/oauth/callback</c> here. So this service needs no
/// public host of its own. <c>Zendesk:RedirectUri</c> still overrides it for a deployment that has one. Each issued state
/// remembers the redirect URL its link was built with, because Zendesk requires the same one at the token exchange and a
/// registration may change it in between.
/// </para>
/// <para>A Singleton, holding the refresh gate and the issued states; the HTTP client comes from the factory per call.</para>
/// </remarks>
public sealed class ZendeskTokenService(
    IOptionsMonitor<ZendeskOptions> options,
    IOAuthTokenStore tokenStore,
    IZendeskRedirectUriStore redirectUriStore,
    IHttpClientFactory httpClientFactory,
    ILogger<ZendeskTokenService> logger)
{
    private const string ConfigurationSource = "configuration";
    private const string InteraSource = "intera";

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IssuedState> issuedStates = new(StringComparer.Ordinal);

    private ZendeskOptions Options => options.CurrentValue;

    public async Task<string> BuildAuthorizationUrlAsync(CancellationToken cancellationToken)
    {
        RequireClient();

        var redirectUri = (await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false)).RedirectUri
            ?? throw new InvalidOperationException(
                "There is no redirect URL yet. Save the Zendesk instance in INTERA, which registers INTERA's callback address here "
                + "(or set Zendesk:RedirectUri for a deployment with its own public host).");

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
            "response_type=code",
            $"client_id={Uri.EscapeDataString(Options.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"scope={Uri.EscapeDataString(NormalizeScopes(Options.Scopes))}",
            $"state={Uri.EscapeDataString(state)}");

        return $"{new Uri(ZendeskBaseUri.Resolve(Options), "oauth/authorizations/new").AbsoluteUri}?{query}";
    }

    /// <summary>The redirect URL a sign-in would use now: the deployment's own if set, otherwise the one INTERA registered.</summary>
    public async Task<ZendeskRedirectUriState> GetRedirectUriAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Options.RedirectUri))
        {
            return new ZendeskRedirectUriState(Options.RedirectUri.Trim(), ConfigurationSource);
        }

        var registered = await redirectUriStore.GetAsync(cancellationToken).ConfigureAwait(false);
        return registered is null ? new ZendeskRedirectUriState(null, null) : new ZendeskRedirectUriState(registered, InteraSource);
    }

    /// <summary>
    /// Stores the redirect URL INTERA registered (the plugin's <c>registerWebhook</c>). Refused when the deployment sets a
    /// different one, so a registration cannot silently lose to configuration; the same value is accepted as a no-op.
    /// </summary>
    public async Task<ZendeskRedirectUriState> RegisterRedirectUriAsync(string? redirectUri, CancellationToken cancellationToken)
    {
        var validated = ZendeskRedirectUri.Validate(redirectUri);

        if (!string.IsNullOrWhiteSpace(Options.RedirectUri))
        {
            var configured = Options.RedirectUri.Trim();
            if (!string.Equals(configured, validated, StringComparison.Ordinal))
            {
                throw new ZendeskRedirectUriConflictException(
                    $"This deployment sets its own redirect URL ({configured}), so INTERA's address ({validated}) is not used. "
                    + "Remove ZENDESK_REDIRECT_URI from .env and redeploy to let INTERA relay the sign-in.");
            }

            return new ZendeskRedirectUriState(configured, ConfigurationSource);
        }

        await redirectUriStore.SaveAsync(validated, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Redirect URL registered by INTERA: {RedirectUri}.", validated);

        return new ZendeskRedirectUriState(validated, InteraSource);
    }

    public async Task ExchangeAuthorizationCodeAsync(string code, string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ZendeskRequestException("The authorization code is missing.");
        }

        if (string.IsNullOrWhiteSpace(state)
            || !issuedStates.TryRemove(state, out var issued)
            || issued.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ZendeskRequestException(
                "The authorization state is unknown or expired. Ask for a new sign-in link (Test connection on the Zendesk "
                + "instance in INTERA, or GET /oauth/authorize-url) — a link is valid once, for 15 minutes.");
        }

        RequireClient();

        var response = await RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = issued.RedirectUri,
                ["scope"] = NormalizeScopes(Options.Scopes)
            },
            cancellationToken).ConfigureAwait(false);

        await SaveAsync(response, previousRefreshToken: null, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Zendesk grant stored (scopes: {Scope}, access token {AccessSeconds} s, refresh token {RefreshSeconds} s).",
            response.Scope, response.ExpiresIn, response.RefreshTokenExpiresIn);
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
    /// Zendesk refused <paramref name="rejectedAccessToken"/> before its stated expiry. Refreshes once — unless another
    /// request already did, in which case the newer token is simply returned.
    /// </summary>
    public async Task<string> RefreshAfterRejectionAsync(string rejectedAccessToken, CancellationToken cancellationToken)
    {
        var token = await RefreshUnderGateAsync(rejectedAccessToken, force: true, cancellationToken).ConfigureAwait(false);
        return token.AccessToken!;
    }

    /// <summary>Refreshes now, whatever the expiry says — the diagnostic <c>POST /oauth/refresh</c> and the keep-alive.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken) =>
        RefreshUnderGateAsync(rejectedAccessToken: null, force: true, cancellationToken);

    public async Task<ZendeskOAuthStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false);
        var redirect = await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false);

        return new ZendeskOAuthStatus(
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

    private async Task<ZendeskTokenResponse> RequestTokenAsync(Dictionary<string, string> body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ZendeskBaseUri.Resolve(Options), "oauth/tokens"))
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClientFactory.CreateClient(Clients.ZendeskClient.HttpClientName)
            .SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        ZendeskTokenResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<ZendeskTokenResponse>(text);
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
            // The refresh token is spent (rotated), expired or revoked, or the code was used already. Only a new
            // authorization helps.
            "invalid_grant" or "access_denied" => new ZendeskAuthorizationException(
                $"Zendesk refused the stored grant ({detail}). Authorise this deployment again: GET /oauth/authorize-url."),
            "invalid_client" or "unauthorized_client" => new ZendeskAuthorizationException(
                $"Zendesk refused the client credentials ({detail}) — check Zendesk:ClientId and Zendesk:ClientSecret."),
            _ when response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ZendeskAuthorizationException($"Zendesk's token endpoint answered {(int)response.StatusCode}: {detail ?? Truncate(text)}"),
            _ => new ZendeskUpstreamException($"Zendesk's token endpoint answered {(int)response.StatusCode}: {detail ?? Truncate(text)}")
        };
    }

    private async Task<OAuthToken> SaveAsync(ZendeskTokenResponse response, string? previousRefreshToken, CancellationToken cancellationToken)
    {
        var token = new OAuthToken(
            Options.ProviderName,
            response.AccessToken,
            // A rotating grant answers a refresh with a new refresh token; keeping the old one is only a fallback for an
            // answer that carries none, which would otherwise leave nothing to refresh with. (A client set to
            // non-expiring tokens has no refresh token at all — then the access token never needs refreshing.)
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
            throw new InvalidOperationException("Zendesk:ClientId and Zendesk:ClientSecret are not configured.");
        }
    }

    private static ZendeskAuthorizationException NotAuthorised() =>
        new("This deployment is not connected to Zendesk yet. Test connection on the Zendesk instance in INTERA gives a "
            + "sign-in link (so does GET /oauth/authorize-url); open it and sign in to Zendesk.");

    private static string NormalizeScopes(string scopes) =>
        string.Join(' ', scopes.Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string text) => text.Length <= 300 ? text : $"{text[..300]}…";

    private sealed record IssuedState(DateTimeOffset ExpiresAt, string RedirectUri);

    private sealed class ZendeskTokenResponse
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

        [JsonPropertyName("refresh_token_expires_in")]
        public int? RefreshTokenExpiresIn { get; init; }

        [JsonPropertyName("error")]
        public string? Error { get; init; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; init; }
    }
}
