using System.Text.Json.Serialization;

namespace Intera.Eurobank.Api.Models;

public sealed class EurobankAccountsResponseDto
{
    [JsonPropertyName("payload")]
    public EurobankAccountsPayloadDto? Payload { get; set; }
}

public sealed class EurobankAccountsPayloadDto
{
    [JsonPropertyName("subscriberName")]
    public string? SubscriberName { get; set; }

    [JsonPropertyName("numberOfRecords")]
    public int? NumberOfRecords { get; set; }

    [JsonPropertyName("accounts")]
    public List<EurobankAccountDto>? Accounts { get; set; }

    [JsonPropertyName("pagination")]
    public EurobankPaginationDto? Pagination { get; set; }
}

public sealed class EurobankPaginationDto
{
    [JsonPropertyName("currentPage")]
    public int? CurrentPage { get; set; }

    [JsonPropertyName("itemsPerPage")]
    public int? ItemsPerPage { get; set; }

    [JsonPropertyName("nextPage")]
    public int? NextPage { get; set; }

    [JsonPropertyName("previousPage")]
    public int? PreviousPage { get; set; }
}
