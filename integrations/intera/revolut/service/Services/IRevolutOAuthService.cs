using Intera.Common.Infrastructure.OAuth;
using Intera.Revolut.Api.Models;

namespace Intera.Revolut.Api.Services;

public interface IRevolutOAuthService
{
    Task<OAuthRedirectUriState> RegisterRedirectUriAsync(string? redirectUri, CancellationToken cancellationToken = default);

    Task<RevolutAuthorizeUrlDto> BuildAuthorizeUrlAsync(CancellationToken cancellationToken = default);

    Task ExchangeAuthorizationCodeAsync(string code, string? state, CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    Task<RevolutOAuthStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);
}
