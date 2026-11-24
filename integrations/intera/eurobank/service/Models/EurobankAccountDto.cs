using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Eurobank.Api.Models;

public sealed class EurobankAccountDto
{
    [JsonPropertyName("accountName")]
    public string? AccountName { get; set; }

    [JsonPropertyName("accountType")]
    public string? AccountType { get; set; }

    [JsonPropertyName("accountTypeDescription")]
    public string? AccountTypeDescription { get; set; }

    [JsonPropertyName("iban")]
    public string? Iban { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("balances")]
    public EurobankBalancesDto? Balances { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class EurobankBalancesDto
{
    [JsonPropertyName("available")]
    public decimal? Available { get; set; }

    [JsonPropertyName("current")]
    public decimal? Current { get; set; }
}
