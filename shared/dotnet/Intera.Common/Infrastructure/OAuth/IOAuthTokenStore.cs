namespace Intera.Common.Infrastructure.OAuth;

public interface IOAuthTokenStore
{
    Task<OAuthToken?> GetAsync(string provider, CancellationToken cancellationToken = default);

    Task SaveAsync(OAuthToken token, CancellationToken cancellationToken = default);
}
