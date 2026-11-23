using Intera.Common.Infrastructure.Http;
using Intera.Salesforce.Api.Infrastructure;
using Intera.Salesforce.Api.Models;
using Intera.Salesforce.Api.Options;
using Microsoft.Extensions.Options;

namespace Intera.Salesforce.Api.Services;

/// <summary>An access token together with the instance host it is valid against.</summary>
public sealed record SalesforceAccessToken(string Token, string InstanceUrl);

public interface ISalesforceOAuthService
{
    /// <summary>Returns a cached token, acquiring one from Salesforce if there is none.</summary>
    Task<SalesforceAccessToken> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops the cached token so the next call mints a new one. Called when Salesforce answers
    /// <c>INVALID_SESSION_ID</c> — the token stopped working before this service thought it would.
    /// </summary>
    void Invalidate();

    /// <summary>Forces a new token regardless of the cached one.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports the cache state without acquiring a token as a side effect.</summary>
    Task<SalesforceOAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Acquires and caches a Salesforce access token via the OAuth2 <b>client-credentials</b> grant.
/// </summary>
/// <remarks>
/// A Singleton so the cached token and its gate are shared, and in memory only, since losing it on
/// a redeploy costs one token request — with two specifics from Salesforce's OAuth implementation:
/// <list type="bullet">
/// <item><b>There is no <c>expires_in</c>.</b> Salesforce ties an access token's life to the org's
/// session timeout and never states it in the response, so the expiry here is a configured TTL and
/// the real recovery is the single retry on <c>INVALID_SESSION_ID</c> in <c>SalesforceClient</c> via
/// <see cref="Invalidate"/>.</item>
/// <item><b>The token carries the host.</b> <c>instance_url</c> comes back with every token and is
/// where the REST calls must then go, so it travels with the token rather than sitting in
/// configuration.</item>
/// </list>
/// <para>
/// Client-credentials, not the username-password flow: Salesforce blocks that one by default for new
/// orgs and it needs the run-as user's actual password, which is a worse thing for a deployment to
/// hold than a secret that can be rotated in Setup without changing anyone's login.
/// </para>
/// </remarks>
public sealed class SalesforceOAuthService(
    IOptions<SalesforceOptions> options,
    IJsonHttpClient httpClient,
    ILogger<SalesforceOAuthService> logger) : ISalesforceOAuthService
{
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private CachedToken? cached;

    private SalesforceOptions Options => options.Value;

    public async Task<SalesforceAccessToken> GetAccessTokenAsync(CancellationToken cancellationToken = default)
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
        // just minted, which costs one extra sign-in. Holding the gate here would instead let a
        // failing call block every other request while it waited for one already in flight.
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

    public Task<SalesforceOAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var token = cached;

        return Task.FromResult(new SalesforceOAuthStatusDto(
            token is not null,
            token?.ExpiresAt,
            token?.AcquiredAt,
            token?.Value.InstanceUrl));
    }

    private bool NeedsRefresh(CachedToken token) => token.ExpiresAt <= DateTimeOffset.UtcNow;

    private async Task<CachedToken> AcquireAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        var loginUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.LoginUrl, "Salesforce:LoginUrl");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = Options.ClientId,
            ["client_secret"] = Options.ClientSecret
        };

        SalesforceTokenResponse? response;
        try
        {
            response = await httpClient.PostFormAsync<SalesforceTokenResponse>(
                $"{loginUri.AbsoluteUri.TrimEnd('/')}/services/oauth2/token",
                form,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpApiException exception)
        {
            var (_, message) = SalesforceErrors.Read(exception.ResponseBody);

            // The token endpoint's own failures are configuration problems with names worth keeping:
            // "invalid_client" is a wrong secret, "inactive_user" a deactivated run-as user, and
            // "unsupported_grant_type" the app not having the client-credentials flow enabled at all.
            throw new SalesforceAuthenticationException(
                $"Salesforce rejected the sign-in: {message ?? $"HTTP {(int)exception.StatusCode}"}");
        }

        if (string.IsNullOrWhiteSpace(response?.AccessToken))
        {
            throw new SalesforceAuthenticationException("Salesforce token response does not contain access_token.");
        }

        var instanceUrl = ResolveInstanceUrl(response);
        var lifetime = TimeSpan.FromMinutes(Math.Max(1, Options.AccessTokenLifetimeMinutes));

        logger.LogInformation("Acquired a Salesforce access token for {InstanceUrl}.", instanceUrl);

        return new CachedToken(
            new SalesforceAccessToken(response.AccessToken, instanceUrl),
            DateTimeOffset.UtcNow.Add(lifetime),
            DateTimeOffset.UtcNow);
    }

    private string ResolveInstanceUrl(SalesforceTokenResponse response)
    {
        if (!string.IsNullOrWhiteSpace(Options.InstanceUrl))
        {
            return HttpsUriValidator.RequireHttpsOrLoopback(Options.InstanceUrl, "Salesforce:InstanceUrl")
                .AbsoluteUri.TrimEnd('/');
        }

        // Salesforce's documented rule: use the instance_url the token came with. It survives a My
        // Domain rename and a sandbox refresh, both of which would strand a hardcoded host.
        if (string.IsNullOrWhiteSpace(response.InstanceUrl))
        {
            throw new SalesforceAuthenticationException(
                "Salesforce token response carried no instance_url, and Salesforce:InstanceUrl is not configured.");
        }

        return HttpsUriValidator.RequireHttpsOrLoopback(response.InstanceUrl, "Salesforce token response instance_url")
            .AbsoluteUri.TrimEnd('/');
    }

    /// <summary>
    /// Checks the three values without which no sign-in is possible.
    /// </summary>
    /// <remarks>
    /// The failure is a <see cref="SalesforceAuthenticationException"/> — a 401 naming the missing
    /// key — rather than an <see cref="InvalidOperationException"/>, which the global filter would
    /// turn into a 500 reading "Internal server error." and nothing else. Naming a configuration key
    /// leaks nothing, and it is the difference between a one-line fix and an afternoon in
    /// <c>docker logs</c>.
    /// </remarks>
    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(Options.LoginUrl))
        {
            throw new SalesforceAuthenticationException("Salesforce:LoginUrl is not configured (the org's My Domain URL).");
        }

        if (string.IsNullOrWhiteSpace(Options.ClientId))
        {
            throw new SalesforceAuthenticationException("Salesforce:ClientId is not configured (the connected app's Consumer Key).");
        }

        if (string.IsNullOrWhiteSpace(Options.ClientSecret))
        {
            throw new SalesforceAuthenticationException("Salesforce:ClientSecret is not configured (the connected app's Consumer Secret).");
        }
    }

    private sealed record CachedToken(SalesforceAccessToken Value, DateTimeOffset ExpiresAt, DateTimeOffset AcquiredAt);
}
