using Intera.Common.Infrastructure.OAuth;
using Intera.Xero.Api.Infrastructure;
using Intera.Xero.Api.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Xero.Api.Services;

public sealed record XeroOAuthStatus(
    bool Configured,
    bool HasAccessToken,
    bool HasRefreshToken,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? UpdatedAt,
    string? Scope,
    string RedirectUri);

/// <summary>
/// The deployment's one Xero grant: the authorization-code flow that creates it, and the refresh that keeps it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Xero rotates the refresh token on every use</b> — each refresh answers with a new one and retires the old
/// (after a 30-minute grace). That is why the token lives in this service's own file (<c>Xero:OAuthTokenFilePath</c>)
/// and not in the integration instance's configuration: nothing in this system could write the new one back there.
/// It is also why refreshes are serialised: two concurrent ones would each spend the same refresh token.
/// </para>
/// <para>
/// <b>The authorization is started by an authenticated call</b> (<c>GET /oauth/authorize-url</c>), and the callback
/// accepts only a <c>state</c> this service issued, once, within fifteen minutes. Without both, anyone able to reach
/// the callback could complete the flow with their own Xero login and replace this deployment's organisation with
/// theirs.
/// </para>
/// <para>
/// A Singleton, holding the refresh gate and the issued states; the HTTP client comes from the factory per call, so
/// no handler is pinned for the process lifetime.
/// </para>
/// </remarks>
public sealed class XeroTokenService(
    IOptionsMonitor<XeroOptions> options,
    IOAuthTokenStore tokenStore,
    IHttpClientFactory httpClientFactory,
    ILogger<XeroTokenService> logger)
{
    public const string HttpClientName = "xero";

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly ConcurrentDictionary<string, DateTimeOffset> issuedStates = new(StringComparer.Ordinal);
    private int grantVersion;

    private XeroOptions Options => options.CurrentValue;

    /// <summary>Changes whenever a new grant is stored, so what was cached for the old one can be dropped.</summary>
    public int GrantVersion => Volatile.Read(ref grantVersion);

    public string BuildAuthorizationUrl()
    {
        RequireClient(requireRedirectUri: true);

        var now = DateTimeOffset.UtcNow;
        foreach (var (issued, expiresAt) in issuedStates)
        {
            if (expiresAt <= now)
            {
                issuedStates.TryRemove(issued, out _);
            }
        }

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        issuedStates[state] = now + StateLifetime;

        var query = string.Join(
            "&",
            "response_type=code",
            $"client_id={Uri.EscapeDataString(Options.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(Options.RedirectUri)}",
            $"scope={Uri.EscapeDataString(NormalizeScopes(Options.Scopes))}",
            $"state={Uri.EscapeDataString(state)}");

        return $"{Options.AuthorizeUrl}?{query}";
    }

    public async Task ExchangeAuthorizationCodeAsync(string code, string? state, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new XeroRequestException("The authorization code is missing.");
        }

        if (string.IsNullOrWhiteSpace(state)
            || !issuedStates.TryRemove(state, out var expiresAt)
            || expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new XeroRequestException(
                "The authorization state is unknown or expired. Start again from GET /oauth/authorize-url — a link is valid once, for 15 minutes.");
        }

        RequireClient(requireRedirectUri: true);

        var response = await RequestTokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = Options.RedirectUri
            },
            cancellationToken).ConfigureAwait(false);

        await SaveAsync(response, previousRefreshToken: null, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref grantVersion);

        logger.LogInformation("Xero grant stored (scopes: {Scope}).", response.Scope);
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
    /// Xero refused <paramref name="rejectedAccessToken"/> before its stated expiry. Refreshes once — unless another
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

    public async Task<XeroOAuthStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false);

        return new XeroOAuthStatus(
            !string.IsNullOrWhiteSpace(Options.ClientId) && !string.IsNullOrWhiteSpace(Options.ClientSecret),
            !string.IsNullOrWhiteSpace(token?.AccessToken),
            !string.IsNullOrWhiteSpace(token?.RefreshToken),
            token?.AccessTokenExpiresAt,
            token?.UpdatedAt,
            token?.Scope,
            Options.RedirectUri);
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

            RequireClient(requireRedirectUri: false);

            var response = await RequestTokenAsync(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
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

    private async Task<XeroTokenResponse> RequestTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Options.TokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Options.ClientId}:{Options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClientFactory.CreateClient(HttpClientName)
            .SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        XeroTokenResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<XeroTokenResponse>(body);
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
            // The refresh token is spent, expired (60 days unused) or revoked, or the code was used already. Only a
            // new authorization helps.
            "invalid_grant" => new XeroAuthorizationException(
                $"Xero refused the stored grant ({detail}). Authorise this deployment again: GET /oauth/authorize-url."),
            "invalid_client" or "unauthorized_client" => new XeroAuthorizationException(
                $"Xero refused the client credentials ({detail}) — check Xero:ClientId and Xero:ClientSecret."),
            _ when response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized => new XeroAuthorizationException(
                $"Xero's token endpoint answered {(int)response.StatusCode}: {detail ?? Truncate(body)}"),
            _ => new XeroUpstreamException(
                $"Xero's token endpoint answered {(int)response.StatusCode}: {detail ?? Truncate(body)}")
        };
    }

    private async Task<OAuthToken> SaveAsync(XeroTokenResponse response, string? previousRefreshToken, CancellationToken cancellationToken)
    {
        var token = new OAuthToken(
            Options.ProviderName,
            response.AccessToken,
            // Xero always answers a refresh with a new refresh token; keeping the old one is only a fallback for an
            // answer that carries none, which would otherwise leave nothing to refresh with.
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

    private void RequireClient(bool requireRedirectUri)
    {
        if (string.IsNullOrWhiteSpace(Options.ClientId) || string.IsNullOrWhiteSpace(Options.ClientSecret))
        {
            throw new InvalidOperationException("Xero:ClientId and Xero:ClientSecret are not configured.");
        }

        if (requireRedirectUri && string.IsNullOrWhiteSpace(Options.RedirectUri))
        {
            throw new InvalidOperationException("Xero:RedirectUri is not configured.");
        }
    }

    private static XeroAuthorizationException NotAuthorised() =>
        new("This deployment is not connected to Xero yet. Open the link from GET /oauth/authorize-url and sign in to Xero.");

    private static string NormalizeScopes(string scopes) =>
        string.Join(' ', scopes.Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string text) => text.Length <= 300 ? text : $"{text[..300]}…";

    private sealed class XeroTokenResponse
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
