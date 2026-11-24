using Intera.Common.Authentication;
using Intera.Common.Controllers;
using Intera.Common.Infrastructure.Query;
using Intera.Common.Models.Query;
using Intera.Common.Swagger;
using Intera.Revolut.Api.Clients;
using Intera.Revolut.Api.Infrastructure;
using Intera.Revolut.Api.Models;
using Intera.Revolut.Api.Models.Query;
using Intera.Revolut.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;

namespace Intera.Revolut.Api.Controllers;

[RequireAccessToken]
[SwaggerManualToken]
public sealed class RevolutController(
    IRevolutBusinessClient revolutClient,
    IOptionsMonitor<RevolutOptions> options) : ApiControllerBase
{
    private static readonly HashSet<string> TransactionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "atm", "card_payment", "card_refund", "card_chargeback", "card_credit", "charge", "charge_refund", "exchange",
        "transfer", "loan", "fee", "refund", "topup", "topup_return", "tax", "tax_refund", "rev_payment"
    };

    private static readonly HashSet<string> TransactionStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "created", "pending", "completed", "declined", "failed", "reverted"
    };

    private static readonly RevolutTransactionLegDto?[] NoLegs = [null];

    [HttpPost("accounts/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryAccountsAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        try
        {
            new InMemoryQueryEngine(RevolutAccountQueryFields.All).Execute([], query);

            var accounts = await revolutClient.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
            var rows = accounts.Select(account => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = account.Id,
                ["name"] = account.Name,
                ["currency"] = account.Currency,
                ["balance"] = account.Balance,
                ["state"] = account.State,
                ["public"] = account.Public
            });

            return QueryInMemoryResponse(rows, query, RevolutAccountQueryFields.All);
        }
        catch (RevolutAuthorizationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
    }

    [HttpPost("balances/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryBalancesAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        try
        {
            new InMemoryQueryEngine(RevolutBalanceQueryFields.All).Execute([], query);

            var balances = await revolutClient.GetBalancesAsync(cancellationToken).ConfigureAwait(false);
            var rows = balances.Select(balance => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["account_id"] = balance.AccountId,
                ["account_name"] = balance.AccountName,
                ["currency"] = balance.Currency,
                ["balance"] = balance.Balance,
                ["state"] = balance.State
            });

            return QueryInMemoryResponse(rows, query, RevolutBalanceQueryFields.All);
        }
        catch (RevolutAuthorizationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
    }

    [HttpPost("exchange-rate/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryExchangeRateAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        try
        {
            new InMemoryQueryEngine(RevolutExchangeRateQueryFields.All).Execute([], query);

            var (from, to, amount) = ExtractExchangeRateParameters(query);
            var exchangeRate = await revolutClient.GetExchangeRateAsync(from, to, amount, cancellationToken).ConfigureAwait(false);

            var rows = exchangeRate is null
                ? []
                : new List<IReadOnlyDictionary<string, object?>>
                {
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["from_currency"] = exchangeRate.From?.Currency,
                        ["to_currency"] = exchangeRate.To?.Currency,
                        ["amount"] = exchangeRate.From?.Amount,
                        ["to_amount"] = exchangeRate.To?.Amount,
                        ["rate"] = exchangeRate.Rate
                    }
                };

            return QueryInMemoryResponse(rows, query, RevolutExchangeRateQueryFields.All);
        }
        catch (RevolutAuthorizationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
    }

    /// <summary>
    /// Transactions at leg grain: a move between the business's own accounts (an exchange included) has two legs,
    /// one per account, and is two rows here, so a sum of <c>amount</c> per account or currency is right and the
    /// number of transactions is <c>countDistinct(transaction_id)</c>.
    /// </summary>
    [HttpPost("transactions/query")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<IReadOnlyDictionary<string, object?>>>>> QueryTransactionsAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] QueryRequest? query,
        CancellationToken cancellationToken)
    {
        try
        {
            new InMemoryQueryEngine(RevolutTransactionQueryFields.All).Execute([], query);

            var request = BuildTransactionsRequest(query, DateTimeOffset.UtcNow);
            if (request is null)
            {
                return QueryInMemoryResponse([], query, RevolutTransactionQueryFields.All);
            }

            var accounts = await revolutClient.GetAccountsAsync(cancellationToken).ConfigureAwait(false);
            var accountNames = accounts
                .Where(static account => account.Id is not null)
                .GroupBy(static account => account.Id!, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First().Name, StringComparer.Ordinal);

            var transactions = await revolutClient.GetTransactionsAsync(request, cancellationToken).ConfigureAwait(false);

            var rows = new List<IReadOnlyDictionary<string, object?>>(transactions.Count);
            foreach (var transaction in transactions)
            {
                IReadOnlyList<RevolutTransactionLegDto?> legs = transaction.Legs is { Count: > 0 } list ? list : NoLegs;
                foreach (var leg in legs)
                {
                    rows.Add(TransactionRow(transaction, leg, accountNames));
                }
            }

            return QueryInMemoryResponse(rows, query, RevolutTransactionQueryFields.All);
        }
        catch (RevolutAuthorizationException exception)
        {
            return Unauthorized(ApiResponse<object?>.Fail(exception.Message));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(ApiResponse<object?>.Fail(exception.Message));
        }
    }

    private RevolutTransactionsRequest? BuildTransactionsRequest(QueryRequest? query, DateTimeOffset now)
    {
        var pushdown = QueryPushdownExtractor.Extract(
            query,
            RevolutTransactionQueryFields.CreatedAt,
            [RevolutTransactionQueryFields.AccountId, RevolutTransactionQueryFields.Type, RevolutTransactionQueryFields.State]);

        DateTimeOffset? to = pushdown.To is { } upperDate ? StartOfDay(upperDate.AddDays(2)) : null;
        if (to > now)
        {
            to = null;
        }

        var from = pushdown.From is { } lowerDate
            ? StartOfDay(lowerDate.AddDays(-1))
            : (to ?? now).AddDays(-Math.Max(1, options.CurrentValue.TransactionsLookbackDays));

        if (to is { } end && from >= end)
        {
            return null;
        }

        var type = pushdown.SingleValue(RevolutTransactionQueryFields.Type);
        var states = pushdown.Values(RevolutTransactionQueryFields.State);

        return new RevolutTransactionsRequest(
            from,
            to,
            pushdown.SingleValue(RevolutTransactionQueryFields.AccountId),
            type is not null && TransactionTypes.Contains(type) ? type.ToLowerInvariant() : null,
            states.Count > 0 && states.All(TransactionStates.Contains)
                ? [.. states.Select(static state => state.ToLowerInvariant())]
                : []);
    }

    private static DateTimeOffset StartOfDay(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static Dictionary<string, object?> TransactionRow(
        RevolutTransactionDto transaction,
        RevolutTransactionLegDto? leg,
        IReadOnlyDictionary<string, string?> accountNames)
    {
        var card = transaction.Card;
        var cardHolder = card is null ? null : $"{card.FirstName} {card.LastName}".Trim();

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["transaction_id"] = transaction.Id,
            ["leg_id"] = leg?.LegId,
            ["type"] = transaction.Type,
            ["state"] = transaction.State,
            ["reason_code"] = transaction.ReasonCode,
            ["request_id"] = transaction.RequestId,
            ["reference"] = transaction.Reference,
            ["created_at"] = transaction.CreatedAt,
            ["updated_at"] = transaction.UpdatedAt,
            ["completed_at"] = transaction.CompletedAt,
            ["scheduled_for"] = transaction.ScheduledFor,
            ["related_transaction_id"] = transaction.RelatedTransactionId,
            ["account_id"] = leg?.AccountId,
            ["account_name"] = leg?.AccountId is { } accountId && accountNames.TryGetValue(accountId, out var name) ? name : null,
            ["amount"] = leg?.Amount,
            ["fee"] = leg?.Fee,
            ["currency"] = leg?.Currency,
            ["bill_amount"] = leg?.BillAmount,
            ["bill_currency"] = leg?.BillCurrency,
            ["balance"] = leg?.Balance,
            ["description"] = leg?.Description,
            ["counterparty_id"] = leg?.Counterparty?.Id,
            ["counterparty_account_id"] = leg?.Counterparty?.AccountId,
            ["counterparty_account_type"] = leg?.Counterparty?.AccountType,
            ["merchant_name"] = transaction.Merchant?.Name,
            ["merchant_city"] = transaction.Merchant?.City,
            ["merchant_category_code"] = transaction.Merchant?.CategoryCode,
            ["merchant_country"] = transaction.Merchant?.Country,
            ["card_number"] = card?.CardNumber,
            ["card_holder"] = string.IsNullOrEmpty(cardHolder) ? null : cardHolder
        };
    }

    private static (string From, string To, decimal Amount) ExtractExchangeRateParameters(QueryRequest? query)
    {
        string? from = null;
        string? to = null;
        var amount = 1m;

        void Visit(FilterNode node)
        {
            switch (node)
            {
                case FilterCondition condition:
                    if (condition.Operator != FilterOperator.Equal)
                    {
                        throw new ArgumentException("Exchange rate filter only supports '=' conditions on 'from_currency', 'to_currency' and 'amount'.");
                    }

                    switch (condition.Field.Trim().ToLowerInvariant())
                    {
                        case "from_currency":
                            from = condition.Value as string;
                            break;
                        case "to_currency":
                            to = condition.Value as string;
                            break;
                        case "amount":
                            amount = condition.Value switch
                            {
                                int i => i,
                                long l => l,
                                decimal d => d,
                                _ => throw new ArgumentException("Filter field 'amount' must be numeric.")
                            };
                            break;
                        default:
                            throw new ArgumentException($"Unsupported filter field '{condition.Field}'. Allowed: from_currency, to_currency, amount.");
                    }

                    break;
                case FilterGroup group when group.Operator == LogicalOperator.And:
                    foreach (var item in group.Items)
                    {
                        Visit(item);
                    }

                    break;
                default:
                    throw new ArgumentException("Exchange rate filter must be a flat condition or an AND group of 'from_currency'/'to_currency'/'amount' conditions.");
            }
        }

        if (query?.Filter is not null)
        {
            Visit(query.Filter);
        }

        foreach (var filter in query?.Filters ?? [])
        {
            Visit(filter);
        }

        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            throw new ArgumentException("Filter must include equality conditions for 'from_currency' and 'to_currency'.");
        }

        return (from, to, amount);
    }
}
