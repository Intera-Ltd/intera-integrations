using System.Text.Json.Serialization;

namespace Intera.BusinessCentral.Api.Models;

public sealed class BusinessCentralCompanyDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("businessProfileId")]
    public string? BusinessProfileId { get; set; }
}
