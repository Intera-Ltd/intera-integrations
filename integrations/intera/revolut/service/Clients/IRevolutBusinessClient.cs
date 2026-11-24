using Intera.Revolut.Api.Models;

namespace Intera.Revolut.Api.Clients;

public interface IRevolutBusinessClient
{
    Task<IReadOnlyList<RevolutAccountDto>> GetAccountsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RevolutBalanceDto>> GetBalancesAsync(CancellationToken cancellationToken = default);

    Task<RevolutExchangeRateDto?> GetExchangeRateAsync(
        string from,
        string to,
        decimal amount,
        CancellationToken cancellationToken = default);

    /// <summary>Every transaction the request covers, all pages read, newest first.</summary>
    Task<IReadOnlyList<RevolutTransactionDto>> GetTransactionsAsync(
        RevolutTransactionsRequest request,
        CancellationToken cancellationToken = default);
}
