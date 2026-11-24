using Intera.Common.Infrastructure.OAuth;
using Intera.Eurobank.Api.Models;

namespace Intera.Eurobank.Api.Services;

public interface IEurobankOAuthService
{
    Task<OAuthRedirectUriState> RegisterRedirectUriAsync(string? redirectUri, CancellationToken cancellationToken = default);

    Task<EurobankAuthorizeUrlDto> BuildAuthorizeUrlAsync(CancellationToken cancellationToken = default);

    Task ExchangeAuthorizationCodeAsync(string code, string? state, CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<EurobankOAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}
