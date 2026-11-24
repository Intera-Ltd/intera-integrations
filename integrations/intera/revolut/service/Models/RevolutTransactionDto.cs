using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Revolut.Api.Models;

/// <summary>One item of <c>GET /transactions</c>. The money lives in <see cref="Legs"/>, not here.</summary>
public sealed class RevolutTransactionDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("scheduled_for")]
    public DateTimeOffset? ScheduledFor { get; set; }

    [JsonPropertyName("related_transaction_id")]
    public string? RelatedTransactionId { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("merchant")]
    public RevolutTransactionMerchantDto? Merchant { get; set; }

    [JsonPropertyName("card")]
    public RevolutTransactionCardDto? Card { get; set; }

    /// <summary>Two legs for a move between the business's own accounts (an exchange included), one otherwise.</summary>
    [JsonPropertyName("legs")]
    public List<RevolutTransactionLegDto>? Legs { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class RevolutTransactionLegDto
{
    [JsonPropertyName("leg_id")]
    public string? LegId { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("fee")]
    public decimal? Fee { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("bill_amount")]
    public decimal? BillAmount { get; set; }

    [JsonPropertyName("bill_currency")]
    public string? BillCurrency { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>The account's balance after this leg.</summary>
    [JsonPropertyName("balance")]
    public decimal? Balance { get; set; }

    [JsonPropertyName("counterparty")]
    public RevolutTransactionCounterpartyDto? Counterparty { get; set; }
}

public sealed class RevolutTransactionCounterpartyDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; set; }

    /// <summary><c>self</c>, <c>revolut</c> or <c>external</c>.</summary>
    [JsonPropertyName("account_type")]
    public string? AccountType { get; set; }
}

public sealed class RevolutTransactionMerchantDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("category_code")]
    public string? CategoryCode { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}

public sealed class RevolutTransactionCardDto
{
    [JsonPropertyName("card_number")]
    public string? CardNumber { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

/// <summary>What <c>GET /transactions</c> is asked for: <see cref="From"/> inclusive, <see cref="To"/> exclusive.</summary>
public sealed record RevolutTransactionsRequest(
    DateTimeOffset From,
    DateTimeOffset? To,
    string? AccountId,
    string? Type,
    IReadOnlyList<string> States);
