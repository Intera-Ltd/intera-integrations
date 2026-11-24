namespace Intera.Common.Authentication;

/// <summary>
/// Validates inbound <c>Authorization: Bearer &lt;token&gt;</c> credentials on behalf of
/// <see cref="AccessTokenAuthenticationHandler"/>.
/// </summary>
public interface IAccessTokenStore
{
    Task<AccessTokenInfo?> TryValidateAsync(string token, CancellationToken cancellationToken);
}

/// <summary>
/// Authenticated caller identity.
/// </summary>
public sealed record AccessTokenInfo(long Id);
