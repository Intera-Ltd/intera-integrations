using System.Text.Json.Serialization;

namespace Intera.BusinessCentral.Api.Models;

/// <summary>
/// Standard BC API v2.0 fields only. Dimensions are NOT flat columns on this entity in the stock API —
/// they live in a separate generalLedgerEntries({id})/dimensionSetLines sub-collection, which this DTO
/// deliberately does not follow (would be an N+1 fetch per entry, on top of the pagination cap already
/// applied to this collection). See BusinessCentralGeneralLedgerEntryQueryFields for the same caveat.
/// </summary>
public sealed class BusinessCentralGeneralLedgerEntryDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("entryNumber")]
    public int? EntryNumber { get; set; }

    [JsonPropertyName("postingDate")]
    public DateOnly? PostingDate { get; set; }

    [JsonPropertyName("documentNumber")]
    public string? DocumentNumber { get; set; }

    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("accountId")]
    public string? AccountId { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("debitAmount")]
    public decimal? DebitAmount { get; set; }

    [JsonPropertyName("creditAmount")]
    public decimal? CreditAmount { get; set; }

    [JsonPropertyName("additionalCurrencyDebitAmount")]
    public decimal? AdditionalCurrencyDebitAmount { get; set; }

    [JsonPropertyName("additionalCurrencyCreditAmount")]
    public decimal? AdditionalCurrencyCreditAmount { get; set; }
}
