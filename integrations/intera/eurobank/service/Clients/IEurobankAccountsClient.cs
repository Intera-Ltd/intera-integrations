using Intera.Eurobank.Api.Models;

namespace Intera.Eurobank.Api.Clients;

/// <summary>
/// Wraps <c>GET {ApiBaseUrl}/accounts</c>. The same Account List response shape is used by both the AISP endpoint
/// (<c>/v2/accounts</c>) and the B2B endpoint (<c>/v2/b2b/accounts</c>), controlled by
/// <see cref="Options.EurobankOptions.ApiBaseUrl"/> and <see cref="Options.EurobankOptions.Scope"/>.
/// </summary>
public interface IEurobankAccountsClient
{
    Task<IReadOnlyList<EurobankAccountDto>> GetAccountsAsync(
        int page = 0,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
