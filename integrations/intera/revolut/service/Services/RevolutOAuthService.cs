using Intera.Common.Infrastructure.Http;
using Intera.Common.Infrastructure.OAuth;
using Intera.Revolut.Api.Infrastructure;
using Intera.Revolut.Api.Models;
using Intera.Revolut.Api.Options;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Intera.Revolut.Api.Services;

/// <summary>
/// Manages the deployment's Revolut Business OAuth 2.0 grant: single-use CSRF-protected authorization links,
/// INTERA webhook redirect URI registration, RSA JWT client assertions, and serialized refresh-token rotation.
/// </summary>
public sealed class RevolutOAuthService(
    IOptionsMonitor<RevolutOptions> options,
    IHttpClientFactory httpClientFactory,
    IClientAssertionFactory clientAssertionFactory,
    IOAuthTokenStore tokenStore,
    IOAuthRedirectUriStore redirectUriStore,
    ILogger<RevolutOAuthService> logger) : IRevolutOAuthService
{
    public const string HttpClientName = "revolut-oauth";

    private const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
    private const int MaxPendingStates = 256;
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IssuedState> issuedStates = new(StringComparer.Ordinal);

    private RevolutOptions Options => options.CurrentValue;

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
                    + "Clear REVOLUT__REDIRECTURI and redeploy to let INTERA relay the sign-in.");
            }

            return new OAuthRedirectUriState(configured, OAuthRedirectUriState.ConfigurationSource);
        }

        await redirectUriStore.SaveAsync(validated, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Revolut redirect URL registered by INTERA: {RedirectUri}.", validated);

        return new OAuthRedirectUriState(validated, OAuthRedirectUriState.InteraSource);
    }

    public async Task<RevolutAuthorizeUrlDto> BuildAuthorizeUrlAsync(CancellationToken cancellationToken = default)
    {
        ValidateClientOptions();
        var portalUri = HttpsUriValidator.RequireHttpsOrLoopback(
            Options.BusinessPortalBaseUrl,
            "Revolut:BusinessPortalBaseUrl");

        var redirectUri = (await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false)).RedirectUri
            ?? throw new InvalidOperationException(
                "There is no redirect URL yet. Save or test the Revolut Business connection in INTERA so Core registers its callback address "
                + "(or set Revolut:RedirectUri for a deployment with its own public host).");

        var now = DateTimeOffset.UtcNow;
        PruneExpiredStates(now);

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        issuedStates[state] = new IssuedState(now + StateLifetime, redirectUri);

        var query = string.Join(
            "&",
            $"client_id={Uri.EscapeDataString(Options.ClientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "response_type=code",
            $"state={Uri.EscapeDataString(state)}");

        var url = $"{portalUri.AbsoluteUri.TrimEnd('/')}/app-confirm?{query}";
        return new RevolutAuthorizeUrlDto(url, redirectUri);
    }

    public async Task ExchangeAuthorizationCodeAsync(
        string code,
        string? state,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new RevolutStateException("The authorization code is missing.");
        }

        if (!TryConsumeState(state, out var issued))
        {
            throw new RevolutStateException(
                "The authorization state is unknown or expired. Generate a new sign-in link from INTERA (valid once for 15 minutes).");
        }

        ValidateClientOptions();
        var tokenUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.TokenEndpoint, "Revolut:TokenEndpoint");

        var form = CreateBaseTokenForm(issued.RedirectUri);
        form["grant_type"] = "authorization_code";
        form["code"] = code;
        form["redirect_uri"] = issued.RedirectUri;

        var httpClient = new JsonHttpClient(httpClientFactory.CreateClient(HttpClientName));
        var response = await httpClient.PostFormAsync<RevolutTokenResponse>(
            tokenUri.AbsoluteUri,
            form,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await SaveTokenResponseAsync(response, previousRefreshToken: null, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Revolut Business OAuth grant stored.");
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

    public async Task<RevolutOAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var token = await tokenStore.GetAsync(Options.ProviderName, cancellationToken).ConfigureAwait(false);
        var redirect = await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false);

        var configured =
            !string.IsNullOrWhiteSpace(Options.ClientId) &&
            !string.IsNullOrWhiteSpace(Options.ClientAssertionIssuer) &&
            (!string.IsNullOrWhiteSpace(Options.PrivateKeyPem) || !string.IsNullOrWhiteSpace(Options.PrivateKeyPath));

        return new RevolutOAuthStatusDto(
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
            var tokenUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.TokenEndpoint, "Revolut:TokenEndpoint");
            var redirect = await GetRedirectUriAsync(cancellationToken).ConfigureAwait(false);

            var form = CreateBaseTokenForm(redirect.RedirectUri);
            form["grant_type"] = "refresh_token";
            form["refresh_token"] = existingToken.RefreshToken;

            var httpClient = new JsonHttpClient(httpClientFactory.CreateClient(HttpClientName));
            var response = await httpClient.PostFormAsync<RevolutTokenResponse>(
                tokenUri.AbsoluteUri,
                form,
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

    private Dictionary<string, string> CreateBaseTokenForm(string? redirectUri)
    {
        var issuer = ResolveClientAssertionIssuer(redirectUri);
        var clientAssertion = !string.IsNullOrWhiteSpace(Options.PrivateKeyPem)
            ? clientAssertionFactory.CreateClientAssertionFromPem(
                Options.ClientId,
                issuer,
                Options.ClientAssertionAudience,
                Options.PrivateKeyPem)
            : clientAssertionFactory.CreateClientAssertion(
                Options.ClientId,
                issuer,
                Options.ClientAssertionAudience,
                ResolvePrivateKeyPath());

        return new Dictionary<string, string>
        {
            ["client_id"] = Options.ClientId,
            ["client_assertion_type"] = ClientAssertionType,
            ["client_assertion"] = clientAssertion
        };
    }

    private async Task<OAuthToken> SaveTokenResponseAsync(
        RevolutTokenResponse? response,
        string? previousRefreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(response?.AccessToken))
        {
            throw new InvalidOperationException("Revolut token response did not contain an access_token.");
        }

        var expiresAt = response.ExpiresIn is null
            ? (DateTimeOffset?)null
            : DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn.Value);

        var token = new OAuthToken(
            Options.ProviderName,
            response.AccessToken,
            string.IsNullOrWhiteSpace(response.RefreshToken) ? previousRefreshToken : response.RefreshToken,
            response.TokenType,
            response.Scope,
            expiresAt,
            DateTimeOffset.UtcNow);

        await tokenStore.SaveAsync(token, cancellationToken).ConfigureAwait(false);
        return token;
    }

    private bool NeedsRefresh(OAuthToken token) =>
        string.IsNullOrWhiteSpace(token.AccessToken)
        || (token.AccessTokenExpiresAt is not null
            && token.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(Options.AccessTokenRefreshSkewSeconds));

    private string ResolveClientAssertionIssuer(string? redirectUri)
    {
        if (!string.IsNullOrWhiteSpace(Options.ClientAssertionIssuer))
        {
            return Options.ClientAssertionIssuer.Trim();
        }

        if (!string.IsNullOrWhiteSpace(redirectUri)
            && Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsed)
            && !string.IsNullOrWhiteSpace(parsed.Host))
        {
            return parsed.Host;
        }

        throw new InvalidOperationException("Revolut:ClientAssertionIssuer is not configured.");
    }

    private string ResolvePrivateKeyPath() =>
        Path.IsPathRooted(Options.PrivateKeyPath)
            ? Options.PrivateKeyPath
            : Path.Combine(AppContext.BaseDirectory, Options.PrivateKeyPath);

    private void ValidateClientOptions()
    {
        if (string.IsNullOrWhiteSpace(Options.ClientId))
        {
            throw new InvalidOperationException("Revolut:ClientId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.ClientAssertionAudience))
        {
            throw new InvalidOperationException("Revolut:ClientAssertionAudience is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.PrivateKeyPem) && string.IsNullOrWhiteSpace(Options.PrivateKeyPath))
        {
            throw new InvalidOperationException("Revolut:PrivateKeyPem is not configured.");
        }
    }

    private static RevolutAuthorizationException NotAuthorized() =>
        new("This deployment is not connected to Revolut Business yet. Test the connection in INTERA to generate a sign-in link.");

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record IssuedState(DateTimeOffset ExpiresAt, string RedirectUri);
}
