using Intera.Common.Infrastructure.Http;
using Intera.Revolut.Api.Models;
using Intera.Revolut.Api.Options;
using Intera.Revolut.Api.Services;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Intera.Revolut.Api.Clients;

public sealed class RevolutBusinessClient(
    IOptionsMonitor<RevolutOptions> options,
    IJsonHttpClient httpClient,
    IRevolutOAuthService oauthService) : ApiClientBase(httpClient), IRevolutBusinessClient
{
    /// <summary>Revolut's maximum page size.</summary>
    private const int TransactionsPageSize = 1000;

    private RevolutOptions Options => options.CurrentValue;

    public async Task<IReadOnlyList<RevolutAccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default)
    {
        var apiBaseUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.ApiBaseUrl, "Revolut:ApiBaseUrl");
        var accessToken = await oauthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var accounts = await HttpClient.GetAsync<List<RevolutAccountDto>>(
            BuildUrl(apiBaseUri.AbsoluteUri, "accounts"),
            accessToken,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return accounts ?? [];
    }

    public async Task<IReadOnlyList<RevolutBalanceDto>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await GetAccountsAsync(cancellationToken).ConfigureAwait(false);

        return accounts
            .Select(account => new RevolutBalanceDto(
                account.Id,
                account.Name,
                account.Currency,
                account.Balance,
                account.State))
            .OrderBy(x => x.Currency)
            .ThenBy(x => x.AccountName)
            .ToList();
    }

    public async Task<RevolutExchangeRateDto?> GetExchangeRateAsync(
        string from,
        string to,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(from))
        {
            throw new ArgumentException("Source currency is required.", nameof(from));
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            throw new ArgumentException("Target currency is required.", nameof(to));
        }

        var apiBaseUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.ApiBaseUrl, "Revolut:ApiBaseUrl");
        var accessToken = await oauthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var amountText = amount.ToString(CultureInfo.InvariantCulture);
        var path = $"rate?from={Uri.EscapeDataString(from.ToUpperInvariant())}&to={Uri.EscapeDataString(to.ToUpperInvariant())}&amount={Uri.EscapeDataString(amountText)}";

        return await HttpClient.GetAsync<RevolutExchangeRateDto>(
            BuildUrl(apiBaseUri.AbsoluteUri, path),
            accessToken,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RevolutTransactionDto>> GetTransactionsAsync(
        RevolutTransactionsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var apiBaseUri = HttpsUriValidator.RequireHttpsOrLoopback(Options.ApiBaseUrl, "Revolut:ApiBaseUrl");
        var accessToken = await oauthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var transactions = new List<RevolutTransactionDto>();
        var to = request.To;

        while (true)
        {
            var page = await HttpClient.GetAsync<List<RevolutTransactionDto>>(
                BuildUrl(apiBaseUri.AbsoluteUri, TransactionsPath(request, to)),
                accessToken,
                cancellationToken: cancellationToken).ConfigureAwait(false) ?? [];

            foreach (var transaction in page)
            {
                if (transaction.Id is null || seen.Add(transaction.Id))
                {
                    transactions.Add(transaction);
                }
            }

            if (page.Count < TransactionsPageSize)
            {
                return transactions;
            }

            var last = page[^1].CreatedAt
                ?? throw new InvalidOperationException("Revolut returned a transaction without created_at; paging cannot continue.");
            var next = TruncateToMillisecond(last).AddMilliseconds(1);

            if (to is { } current && next >= current)
            {
                throw new InvalidOperationException(
                    $"More than {TransactionsPageSize} Revolut transactions share created_at {last:O}; paging cannot advance past them.");
            }

            to = next;
        }
    }

    private static string TransactionsPath(RevolutTransactionsRequest request, DateTimeOffset? to)
    {
        var query = new List<string>
        {
            $"from={FormatInstant(request.From)}",
            $"count={TransactionsPageSize.ToString(CultureInfo.InvariantCulture)}"
        };

        if (to is { } upper)
        {
            query.Add($"to={FormatInstant(upper)}");
        }

        if (!string.IsNullOrWhiteSpace(request.AccountId))
        {
            query.Add($"account={Uri.EscapeDataString(request.AccountId)}");
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            query.Add($"type={Uri.EscapeDataString(request.Type)}");
        }

        foreach (var state in request.States)
        {
            query.Add($"state={Uri.EscapeDataString(state)}");
        }

        return $"transactions?{string.Join('&', query)}";
    }

    private static string FormatInstant(DateTimeOffset value) =>
        Uri.EscapeDataString(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));

    private static DateTimeOffset TruncateToMillisecond(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Offset);
}
