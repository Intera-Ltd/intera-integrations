using Intera.BusinessCentral.Api.Models;
using Intera.BusinessCentral.Api.Options;
using Intera.Common.Infrastructure.Http;
using Microsoft.Extensions.Options;

namespace Intera.BusinessCentral.Api.Services;

/// <summary>
/// Acquires and caches a Microsoft Entra ID access token via the OAuth 2.0 client-credentials
/// (service-to-service) grant. Registered as a Singleton so the cached token and its guarding
/// semaphore are shared across requests.
/// </summary>
public sealed class BusinessCentralOAuthService(
    IOptions<BusinessCentralOptions> options,
    IJsonHttpClient httpClient) : IBusinessCentralOAuthService
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private CachedToken? cached;

    private BusinessCentralOptions Options => options.Value;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = cached;
        if (token is not null && !NeedsRefresh(token))
        {
            return token.AccessToken;
        }

        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            token = cached;
            if (token is null || NeedsRefresh(token))
            {
                token = await AcquireTokenAsync(cancellationToken).ConfigureAwait(false);
                cached = token;
            }
        }
        finally
        {
            refreshGate.Release();
        }

        return token.AccessToken;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = await AcquireTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public Task<BusinessCentralOAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var token = cached;

        return Task.FromResult(new BusinessCentralOAuthStatusDto(
            token is not null,
            token?.ExpiresAt,
            token?.AcquiredAt));
    }

    private bool NeedsRefresh(CachedToken token) =>
        token.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(Options.AccessTokenRefreshSkewSeconds);

    private async Task<CachedToken> AcquireTokenAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Options.ClientId,
            ["client_secret"] = Options.ClientSecret,
            ["scope"] = Options.Scope
        };

        var tokenEndpoint = HttpsUriValidator.RequireHttpsOrLoopback(ResolveTokenEndpoint(), "BusinessCentral:TokenEndpoint").ToString();
        var response = await httpClient.PostFormAsync<BusinessCentralTokenResponse>(
            tokenEndpoint,
            form,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(response?.AccessToken))
        {
            throw new InvalidOperationException("Microsoft Entra ID token response does not contain access_token.");
        }

        var expiresIn = response.ExpiresIn is > 0 ? response.ExpiresIn.Value : 3600;

        return new CachedToken(response.AccessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn), DateTimeOffset.UtcNow);
    }

    private string ResolveTokenEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(Options.TokenEndpoint))
        {
            return Options.TokenEndpoint.Trim();
        }

        if (string.IsNullOrWhiteSpace(Options.TenantId))
        {
            throw new InvalidOperationException("BusinessCentral:TenantId is not configured.");
        }

        return $"https://login.microsoftonline.com/{Uri.EscapeDataString(Options.TenantId.Trim())}/oauth2/v2.0/token";
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(Options.ClientId))
        {
            throw new InvalidOperationException("BusinessCentral:ClientId is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.ClientSecret))
        {
            throw new InvalidOperationException("BusinessCentral:ClientSecret is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Options.Scope))
        {
            throw new InvalidOperationException("BusinessCentral:Scope is not configured.");
        }
    }

    private sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresAt, DateTimeOffset AcquiredAt);
}
