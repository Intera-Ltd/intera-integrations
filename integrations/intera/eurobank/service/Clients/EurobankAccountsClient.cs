using Intera.Common.Infrastructure.Http;
using Intera.Eurobank.Api.Models;
using Intera.Eurobank.Api.Options;
using Intera.Eurobank.Api.Services;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Intera.Eurobank.Api.Clients;

public sealed class EurobankAccountsClient(
    IOptionsMonitor<EurobankOptions> options,
    IJsonHttpClient httpClient,
    IEurobankOAuthService oauthService) : ApiClientBase(httpClient), IEurobankAccountsClient
{
    private EurobankOptions Options => options.CurrentValue;

    public async Task<IReadOnlyList<EurobankAccountDto>> GetAccountsAsync(
        int page = 0,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var apiBaseUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.ApiBaseUrl, "Eurobank:ApiBaseUrl");
        var accessToken = await oauthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var path = $"accounts?page={page.ToString(CultureInfo.InvariantCulture)}&limit={limit.ToString(CultureInfo.InvariantCulture)}";

        var response = await HttpClient.GetAsync<EurobankAccountsResponseDto>(
            BuildUrl(apiBaseUri.AbsoluteUri, path),
            accessToken,
            headers: new Dictionary<string, string> { ["x-client-id"] = Options.ClientId },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response?.Payload?.Accounts ?? [];
    }
}
