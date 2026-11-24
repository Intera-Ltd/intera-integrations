using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intera.Revolut.Api.Models;

public sealed class RevolutExchangeRateAmountDto
{
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }
}

public sealed class RevolutExchangeRateDto
{
    [JsonPropertyName("from")]
    public RevolutExchangeRateAmountDto? From { get; set; }

    [JsonPropertyName("to")]
    public RevolutExchangeRateAmountDto? To { get; set; }

    [JsonPropertyName("rate")]
    public decimal? Rate { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}
