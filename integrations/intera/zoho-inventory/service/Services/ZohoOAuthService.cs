using Intera.Common.Infrastructure.Http;
using Intera.ZohoInventory.Api.Infrastructure;
using Intera.ZohoInventory.Api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

namespace Intera.ZohoInventory.Api.Services;

/// <summary>An access token together with the API root it is valid against.</summary>
public sealed record ZohoAccessToken(string Token, string ApiBaseUrl);

public sealed record ZohoOAuthStatusDto(
    bool HasToken,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? AcquiredAt,
    string? ApiBaseUrl,
    string AccountsUrl,
    string OrganizationId);

public interface IZohoOAuthService
{
    /// <summary>Returns a cached access token, trading the refresh token for one if there is none.</summary>
    Task<ZohoAccessToken> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached token so the next call mints a new one. Called when Zoho answers 401 — the
    /// token stopped working before this service thought it would.
    /// </summary>
    void Invalidate();

    /// <summary>Forces a new access token regardless of the cached one.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports the cache state without acquiring a token as a side effect.</summary>
    ZohoOAuthStatusDto GetStatus();
}

/// <summary>
/// Trades the deployment's refresh token for an access token and caches it in memory.
/// </summary>
/// <remarks>
/// <para>
/// A Singleton, so the cached token and its gate are shared by every request, and in memory only:
/// the refresh token is in configuration and does not rotate, so a lost cache costs one token
/// request and nothing needs to survive a restart (unlike rotating refresh tokens that must be persisted to disk).
/// </para>
/// <para>
/// Caching is not only about latency here. Zoho allows a refresh token to mint only a handful of
/// access tokens per ten minutes, so a service that asked for a new one per request would lock
/// itself out under the first dashboard that opened six widgets.
/// </para>
/// <para>
/// <b>The token endpoint reports failure with HTTP 200.</b> A wrong client secret or a revoked
/// refresh token comes back as <c>{"error":"invalid_client"}</c> / <c>{"error":"invalid_code"}</c>
/// with a success status, so the body is checked before the status is trusted.
/// </para>
/// </remarks>
public sealed class ZohoOAuthService(
    IOptions<ZohoInventoryOptions> options,
    IJsonHttpClient httpClient,
    ILogger<ZohoOAuthService> logger) : IZohoOAuthService
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private CachedToken? cached;

    private ZohoInventoryOptions Options => options.Value;

    public async Task<ZohoAccessToken> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = cached;
        if (token is not null && !NeedsRefresh(token))
        {
            return token.Value;
        }

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            token = cached;
            if (token is null || NeedsRefresh(token))
            {
                token = await AcquireAsync(cancellationToken).ConfigureAwait(false);
                cached = token;
            }
        }
        finally
        {
            refreshGate.Release();
        }

        return token.Value;
    }

    public void Invalidate()
    {
        // A plain write, not taken under the gate: the worst a race can do is drop a token that was
        // just minted, which costs one extra token request.
        cached = null;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = await AcquireAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public ZohoOAuthStatusDto GetStatus()
    {
        var token = cached;

        return new ZohoOAuthStatusDto(
            token is not null,
            token?.ExpiresAt,
            token?.AcquiredAt,
            token?.Value.ApiBaseUrl,
            ZohoDataCenter.ResolveAccountsUrl(Options),
            Options.OrganizationId);
    }

    private bool NeedsRefresh(CachedToken token) =>
        token.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, Options.AccessTokenRefreshSkewSeconds));

    private async Task<CachedToken> AcquireAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        var accountsUrl = ZohoDataCenter.ResolveAccountsUrl(Options);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = Options.ClientId,
            ["client_secret"] = Options.ClientSecret,
            ["refresh_token"] = Options.RefreshToken
        };

        ZohoTokenResponse? response;
        try
        {
            response = await httpClient.PostFormAsync<ZohoTokenResponse>(
                $"{accountsUrl}/oauth/v2/token",
                form,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpApiException exception)
        {
            var (_, message) = ZohoErrors.Read(exception.ResponseBody);
            throw new ZohoAuthenticationException(
                $"Zoho accounts ({accountsUrl}) rejected the refresh token: {message ?? $"HTTP {(int)exception.StatusCode}"}");
        }

        if (!string.IsNullOrWhiteSpace(response?.Error))
        {
            // "invalid_client": the client id/secret are wrong, or belong to another data center.
            // "invalid_code": the refresh token was revoked, or was never valid on this data center.
            throw new ZohoAuthenticationException(
                $"Zoho accounts ({accountsUrl}) rejected the refresh token: {response.Error}. "
                + "Check ZohoInventory:ClientId/ClientSecret/RefreshToken and that ZohoInventory:DataCenter matches the organization's own domain.");
        }

        if (string.IsNullOrWhiteSpace(response?.AccessToken))
        {
            throw new ZohoAuthenticationException("Zoho token response does not contain access_token.");
        }

        var apiBaseUrl = ResolveApiBaseUrl(response);
        var lifetime = TimeSpan.FromSeconds(response.ExpiresIn is > 0 ? response.ExpiresIn.Value : 3600);
        var now = DateTimeOffset.UtcNow;

        logger.LogInformation("Acquired a Zoho access token for {ApiBaseUrl}, valid for {Lifetime}.", apiBaseUrl, lifetime);

        return new CachedToken(new ZohoAccessToken(response.AccessToken, apiBaseUrl), now.Add(lifetime), now);
    }

    private string ResolveApiBaseUrl(ZohoTokenResponse response)
    {
        if (!string.IsNullOrWhiteSpace(Options.ApiBaseUrl))
        {
            return Options.ApiBaseUrl.TrimEnd('/');
        }

        return !string.IsNullOrWhiteSpace(response.ApiDomain)
            ? $"{response.ApiDomain.TrimEnd('/')}/inventory/v1"
            : ZohoDataCenter.ResolveDefaultApiBaseUrl(Options);
    }

    /// <summary>
    /// A 401 naming the missing key rather than the global filter's anonymous 500: naming a
    /// configuration key leaks nothing, and it is the difference between a one-line fix and an
    /// afternoon in <c>docker logs</c>.
    /// </summary>
    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(Options.ClientId))
        {
            throw new ZohoAuthenticationException("ZohoInventory:ClientId is not configured (the Self Client's Client ID).");
        }

        if (string.IsNullOrWhiteSpace(Options.ClientSecret))
        {
            throw new ZohoAuthenticationException("ZohoInventory:ClientSecret is not configured (the Self Client's Client Secret).");
        }

        if (string.IsNullOrWhiteSpace(Options.RefreshToken))
        {
            throw new ZohoAuthenticationException("ZohoInventory:RefreshToken is not configured.");
        }
    }

    private sealed record CachedToken(ZohoAccessToken Value, DateTimeOffset ExpiresAt, DateTimeOffset AcquiredAt);

    private sealed class ZohoTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("api_domain")]
        public string? ApiDomain { get; set; }

        [JsonPropertyName("expires_in")]
        public int? ExpiresIn { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
