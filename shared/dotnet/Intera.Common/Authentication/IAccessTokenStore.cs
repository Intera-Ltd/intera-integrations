namespace Intera.Common.Authentication;

/// <summary>
/// Validates an inbound <c>X-Access-Token</c>/<c>Bearer</c> value on behalf of
/// <see cref="AccessTokenAuthenticationHandler"/>.
/// </summary>
public interface IAccessTokenStore
{
    /// <summary>
    /// Whether an inbound access token is configured. When <see langword="false"/> (for example,
    /// inside an isolated Marketplace container network where <c>ApiAccessToken</c> is unset),
    /// internal requests authenticate without an <c>X-Access-Token</c> header.
    /// </summary>
    bool RequiresToken { get; }

    Task<AccessTokenInfo?> TryValidateAsync(string token, CancellationToken cancellationToken);
}

/// <summary>
/// Authenticated caller identity.
/// </summary>
public sealed record AccessTokenInfo(long Id);
