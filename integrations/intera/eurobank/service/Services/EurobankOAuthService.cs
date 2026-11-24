using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Intera.Eurobank.Api.Infrastructure;
using Intera.Eurobank.Api.Models;
using Intera.Eurobank.Api.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Intera.Eurobank.Api.Services;

/// <summary>
/// Manages the deployment's Eurobank v2 OAuth 2.0 grant: single-use CSRF-protected authorization links,
/// INTERA webhook redirect URI registration, HTTP Basic token exchange, and serialized refresh-token rotation.
/// </summary>
public sealed class EurobankOAuthService(
    IOptionsMonitor<EurobankOptions> options,
    IHttpClientFactory httpClientFactory,
    IOAuthTokenStore tokenStore,
    IOAuthRedirectUriStore redirectUriStore,
    ILogger<EurobankOAuthService> logger) : IEurobankOAuthService
{
    public const string HttpClientName = "eurobank-oauth";

    private const int MaxPendingStates = 256;
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IssuedState> issuedStates = new(StringComparer.Ordinal);

    private EurobankOptions Options => options.CurrentValue;

    public async Task<OAuthRedirectUriState> RegisterRedirectUriAsync(
        string? redirectUri,
        CancellationToken cancellationToken = default)
    {
        var validated = OAuthRedirectUriValidator.Validate(redirectUri);

        if (!string.IsNullOrWhiteSpace(Options.RedirectUri))
        {
            var configured = Options.RedirectUri.Trim();
            if (!string.Equals(configured, validated, StringComparison.Ordinal))
            {
                throw new OAuthRedirectUriConflictException(
                    $"This deployment configures its own redirect URL ({configured}), so INTERA's callback address ({validated}) is not used. "
                    + "Clear EUROBANK__REDIRECTURI and redeploy to let INTERA relay the sign-in.");
            }

            return new OAuthRedirectUriState(configured, OAuthRedirectUriState.ConfigurationSource);
        }

        await redirectUriStore.SaveAsync(validated, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Eurobank redirect URL registered by INTERA: {RedirectUri}.", validated);

        return new OAuthRedirectUriState(validated, OAuthRedirectUriState.InteraSource);
    }

    public async Task<EurobankAuthorizeUrlDto> BuildAuthorizeUrlAsync(CancellationToken cancellationToken = default)
    {
        ValidateClientOptions();
        var authBaseUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.AuthBaseUrl, "Eurobank:AuthBaseUrl");

        var redirectUri = (await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false)).RedirectUri
            ?? throw new InvalidOperationException(
                "There is no redirect URL yet. Save or test the Eurobank connection in INTERA so Core registers its callback address "
                + "(or set Eurobank:RedirectUri for a deployment with its own public host).");

        var now = DateTimeOffset.UtcNow;
        PruneExpiredStates(now);

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        issuedStates[state] = new IssuedState(now + StateLifetime, redirectUri);

        var query = string.Join(
            "&",
            "response_type=code",
            $"client_id={Uri.EscapeDataString(Options.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            $"scope={Uri.EscapeDataString(Options.Scope.Trim())}",
            $"state={Uri.EscapeDataString(state)}");

        var url = $"{authBaseUri.AbsoluteUri.TrimEnd('/')}/v2/oauth2/auth?{query}";
        return new EurobankAuthorizeUrlDto(url, redirectUri);
    }

    public async Task ExchangeAuthorizationCodeAsync(
        string code,
        string? state,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new EurobankStateException("The authorization code is missing.");
        }

        if (!TryConsumeState(state, out var issued))
        {
            throw new EurobankStateException(
                "The authorization state is unknown or expired. Generate a new sign-in link from INTERA (valid once for 15 minutes).");
        }

        ValidateClientOptions();
        var authBaseUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.AuthBaseUrl, "Eurobank:AuthBaseUrl");

        var query = string.Join(
            "&",
            "grant_type=authorization_code",
            $"redirect_uri={Uri.EscapeDataString(issued.RedirectUri)}",
            $"code={Uri.EscapeDataString(code)}");

        var httpClient = new JsonHttpClient(httpClientFactory.CreateClient(HttpClientName));
        var response = await httpClient.SendAsync<EurobankTokenResponse>(
            HttpMethod.Post,
            $"{authBaseUri.AbsoluteUri.TrimEnd('/')}/v2/token/exchange?{query}",
            content: null,
            headers: BuildBasicAuthHeader(),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await SaveTokenResponseAsync(response, previousRefreshToken: null, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Eurobank OAuth grant stored.");
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshUnderGateAsync(force: true, cancellationToken);

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false)
            ?? throw NotAuthorized();

        if (!NeedsRefresh(token))
        {
            return token.AccessToken!;
        }

        var refreshed = await RefreshUnderGateAsync(force: false, cancellationToken).ConfigureAwait(false);
        return refreshed.AccessToken!;
    }

    public async Task<EurobankOAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false);
        var redirect = await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false);

        var configured =
            !string.IsNullOrWhiteSpace(Options.ClientId) &&
            !string.IsNullOrWhiteSpace(Options.ClientSecret);

        return new EurobankOAuthStatusDto(
            configured,
            !string.IsNullOrWhiteSpace(token?.AccessToken),
            !string.IsNullOrWhiteSpace(token?.RefreshToken),
            token?.AccessTokenExpiresAt,
            token?.UpdatedAt,
            redirect.RedirectUri ?? string.Empty,
            redirect.Source);
    }

    private async Task<OAuthToken> RefreshUnderGateAsync(bool force, CancellationToken cancellationToken)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existingToken = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false)
                ?? throw NotAuthorized();

            if (!force && !NeedsRefresh(existingToken))
            {
                return existingToken;
            }

            if (string.IsNullOrWhiteSpace(existingToken.RefreshToken))
            {
                throw NotAuthorized();
            }

            ValidateClientOptions();
            var authBaseUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.AuthBaseUrl, "Eurobank:AuthBaseUrl");

            var query = string.Join(
                "&",
                "grant_type=refresh_token",
                $"refresh_token={Uri.EscapeDataString(existingToken.RefreshToken)}");

            var httpClient = new JsonHttpClient(httpClientFactory.CreateClient(HttpClientName));
            var response = await httpClient.SendAsync<EurobankTokenResponse>(
                HttpMethod.Post,
                $"{authBaseUri.AbsoluteUri.TrimEnd('/')}/v2/token?{query}",
                content: null,
                headers: BuildBasicAuthHeader(),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return await SaveTokenResponseAsync(response, existingToken.RefreshToken, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task<OAuthRedirectUriState> GetRedirectUriAsync(CancellationToken cancellationToken)
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

    private void PruneExpiredStates(DateTimeOffset now)
    {
        foreach (var (key, entry) in issuedStates)
        {
            if (entry.ExpiresAt <= now)
            {
                issuedStates.TryRemove(key, out _);
            }
        }

        while (issuedStates.Count >= MaxPendingStates)
        {
            var oldest = issuedStates.OrderBy(static pair => pair.Value.ExpiresAt).FirstOrDefault();
            if (oldest.Key is null || !issuedStates.TryRemove(oldest.Key, out _))
            {
                break;
            }
        }
    }

    private bool TryConsumeState(string? providedState, out IssuedState issued)
    {
        issued = default!;
        if (string.IsNullOrWhiteSpace(providedState))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedState));
        string? matchedKey = null;

        foreach (var (key, entry) in issuedStates)
        {
            if (entry.ExpiresAt <= now)
            {
                issuedStates.TryRemove(key, out _);
                continue;
            }

            var candidateHash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            if (CryptographicOperations.FixedTimeEquals(providedHash, candidateHash))
            {
                matchedKey = key;
            }
        }

        if (matchedKey is null || !issuedStates.TryRemove(matchedKey, out var removed) || removed.ExpiresAt <= now)
        {
            return false;
        }

        issued = removed;
        return true;
    }

    private IReadOnlyDictionary<string, string> BuildBasicAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Options.ClientId}:{Options.ClientSecret}"));
        return new Dictionary<string, string> { ["Authorization"] = $"Basic {credentials}" };
    }

    private async Task<OAuthToken> SaveTokenResponseAsync(
        EurobankTokenResponse? response,
        string? previousRefreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(response?.AccessToken))
        {
            throw new InvalidOperationException("Eurobank token response did not contain an access_token.");
        }

        var expiresAt = response.ExpiresAt is null
            ? (DateTimeOffset?)null
            : DateTimeOffset.FromUnixTimeMilliseconds(response.ExpiresAt.Value);

        var token = new OAuthToken(
            Options.ProviderName,
            response.AccessToken,
            string.IsNullOrWhiteSpace(response.RefreshToken) ? previousRefreshToken : response.RefreshToken,
            response.TokenType,
            response.Scope is null ? null : string.Join(",", response.Scope),
            expiresAt,
            DateTimeOffset.UtcNow);

        await tokenStore.SaveAsync(token, cancellationToken).ConfigureAwait(false);
        return token;
    }

    private bool NeedsRefresh(OAuthToken token) =>
        string.IsNullOrWhiteSpace(token.AccessToken)
        || (token.AccessTokenExpiresAt is not null
            && token.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(Options.AccessTokenRefreshSkewSeconds));

    private void ValidateClientOptions()
    {
        if (string.IsNullOrWhiteSpace(Options.ClientId))
        {
            throw new InvalidOperationException("Eurobank:ClientId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.ClientSecret))
        {
            throw new InvalidOperationException("Eurobank:ClientSecret is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.Scope))
        {
            throw new InvalidOperationException("Eurobank:Scope is not configured.");
        }
    }

    private static EurobankAuthorizationException NotAuthorized() =>
        new("This deployment is not connected to Eurobank yet. Test the connection in INTERA to generate a sign-in link.");

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record IssuedState(DateTimeOffset ExpiresAt, string RedirectUri);
}
