using Intera.Common.Infrastructure.Query;

namespace Intera.Revolut.Api.Models.Query;

/// <summary>One row is one leg of a transaction; the transaction's own fields repeat on each of its legs.</summary>
public static class RevolutTransactionQueryFields
{
    public const string CreatedAt = "created_at";
    public const string AccountId = "account_id";
    public const string Type = "type";
    public const string State = "state";

    public static readonly IReadOnlyDictionary<string, InMemoryQueryField> All = InMemoryQueryFieldsBuilder.Build(
        "transaction_id",
        "leg_id",
        Type,
        State,
        "reason_code",
        "request_id",
        "reference",
        CreatedAt,
        "updated_at",
        "completed_at",
        "scheduled_for",
        "related_transaction_id",
        AccountId,
        "account_name",
        "amount",
        "fee",
        "currency",
        "bill_amount",
        "bill_currency",
        "balance",
        "description",
        "counterparty_id",
        "counterparty_account_id",
        "counterparty_account_type",
        "merchant_name",
        "merchant_city",
        "merchant_category_code",
        "merchant_country",
        "card_number",
        "card_holder");
}
