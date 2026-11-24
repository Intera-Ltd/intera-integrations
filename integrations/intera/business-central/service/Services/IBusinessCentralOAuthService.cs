using Intera.BusinessCentral.Api.Models;

namespace Intera.BusinessCentral.Api.Services;

public interface IBusinessCentralOAuthService
{
    /// <summary>Returns a cached access token, acquiring/refreshing one from Entra ID if needed.</summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Forces a new token to be acquired regardless of the cached one's expiry.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports the current cache state without acquiring a token as a side effect.</summary>
    Task<BusinessCentralOAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}
